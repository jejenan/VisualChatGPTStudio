﻿using Community.VisualStudio.Toolkit;
using ICSharpCode.AvalonEdit;
using JeffPires.VisualChatGPTStudio.Options;
using JeffPires.VisualChatGPTStudio.Utils;
using JeffPires.VisualChatGPTStudio.Utils.CodeCompletion;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VisualChatGPTStudioShared.Utils;
using Clipboard = System.Windows.Clipboard;
using Constants = JeffPires.VisualChatGPTStudio.Utils.Constants;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace JeffPires.VisualChatGPTStudio.ToolWindows
{
    /// <summary>
    /// Interaction logic for TerminalWindowControl.
    /// </summary>
    public partial class TerminalWindowControl : UserControl
    {
        #region Properties

        private OptionPageGridGeneral options;
        private Package package;
        private bool firstIteration;
        private bool responseStarted;
        private CancellationTokenSource cancellationTokenSource;
        private bool removeCodeTagsFromOpenAIResponses;
        private CompletionManager completionManager;

        #endregion Properties

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="TerminalWindowControl"/> class.
        /// </summary>
        public TerminalWindowControl()
        {
            //It is necessary for the MdXaml library load successfully
            MdXaml.MarkdownScrollViewer _ = new();

            this.InitializeComponent();

            txtRequest.TextArea.TextEntering += txtRequest_TextEntering;
            txtRequest.TextArea.TextEntered += txtRequest_TextEntered;
        }

        #endregion Constructors

        #region Event Handlers

        /// <summary>
        /// Handles the text entered event for the request text box, 
        /// passing the entered text to the CompletionManager for processing.
        /// </summary>
        /// <param name="sender">The source of the event, typically the text box.</param>
        /// <param name="e">The event data containing the text that was entered.</param>
        private async void txtRequest_TextEntered(object sender, TextCompositionEventArgs e)
        {
            await completionManager.HandleTextEnteredAsync(e);
        }

        /// <summary>
        /// Handles the text entering event for the request text box, delegating the processing to the CompletionManager.
        /// </summary>
        /// <param name="sender">The source of the event, typically the text box.</param>
        /// <param name="e">The event data containing information about the text composition.</param>
        private void txtRequest_TextEntering(object sender, TextCompositionEventArgs e)
        {
            completionManager.HandleTextEntering(e);
        }

        /// <summary>
        /// Handles the PreviewMouseWheel event for the txtResponse control, scrolling the associated ScrollViewer based on the mouse wheel delta.
        /// </summary>
        private void txtResponse_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                MdXaml.MarkdownScrollViewer mdXaml = (MdXaml.MarkdownScrollViewer)sender;

                List<TextEditor> textEditors = FindMarkDownCodeTextEditors(mdXaml);

                if (textEditors != null)
                {
                    foreach (TextEditor textEditor in textEditors)
                    {
                        ScrollViewer scrollViewerEditor = textEditor.Template.FindName("PART_ScrollViewer", textEditor) as ScrollViewer;

                        if (scrollViewerEditor != null)
                        {
                            scrollViewerEditor.ScrollToHorizontalOffset(scrollViewerEditor.HorizontalOffset - e.Delta);
                        }
                    }
                }

                e.Handled = true;
            }
            else
            {
                e.Handled = false;
            }
        }

        /// <summary>
        /// Handles the Click event of the btnRequestSend control.
        /// </summary>
        public async void SendRequest(Object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (!options.AzureEntraIdAuthentication && string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    MessageBox.Show(Constants.MESSAGE_SET_API_KEY, Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);

                    package.ShowOptionPage(typeof(OptionPageGridGeneral));

                    return;
                }

                firstIteration = true;
                responseStarted = false;

                if (string.IsNullOrWhiteSpace(txtRequest.Text))
                {
                    MessageBox.Show(Constants.MESSAGE_WRITE_REQUEST, Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                EnableDisableButtons(false, true);

                txtResponse.Markdown = string.Empty;

                cancellationTokenSource = new CancellationTokenSource();

                removeCodeTagsFromOpenAIResponses = false;

                string request = await completionManager.ReplaceReferencesAsync(txtRequest.Text);

                string result = await ChatGPT.GetResponseAsync(options, options.ToolWindowSystemMessage, request, options.StopSequences.Split([','], StringSplitOptions.RemoveEmptyEntries), cancellationTokenSource.Token);

                ResultHandler(result);
            }
            catch (OperationCanceledException)
            {
                EnableDisableButtons(true, false);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);

                EnableDisableButtons(true, false);

                MessageBox.Show(ex.Message, Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Handles the click event of the Generate Git Comment button. It generates a Git comment based on the current changes using ChatGPT.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private async void btnGenerateGitComment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnableDisableButtons(false, true);

                string changes = GitChanges.GetCurrentChanges();

                if (string.IsNullOrWhiteSpace(changes))
                {
                    MessageBox.Show("No changes found.", Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Information);

                    return;
                }

                txtRequest.Text = string.Concat(options.GenerateGitCommentCommand, Environment.NewLine, Environment.NewLine, changes);
                txtResponse.Markdown = string.Empty;

                cancellationTokenSource = new CancellationTokenSource();

                removeCodeTagsFromOpenAIResponses = true;

                string comment = await ChatGPT.GetResponseAsync(options, options.GenerateGitCommentCommand, changes, options.StopSequences.Split([','], StringSplitOptions.RemoveEmptyEntries), cancellationTokenSource.Token);

                ResultHandler(comment);
            }
            catch (OperationCanceledException)
            {
                //Do nothing
            }
            catch (ArgumentNullException)
            {
                MessageBox.Show("Git repository not found.", Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);

                MessageBox.Show(ex.Message, Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                EnableDisableButtons(true, false);
            }
        }

        /// <summary>
        /// Cancels the request.
        /// </summary>
        public async void CancelRequest(Object sender, ExecutedRoutedEventArgs e)
        {
            btnCancel.IsEnabled = false;

            cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Handles the Click event of the btnRequestPast control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RoutedEventArgs"/> instance containing the event data.</param>
        [STAThread]
        private void btnRequestPast_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                txtRequest.AppendText(Clipboard.GetText());
            }
        }

        /// <summary>
        /// Clears the request text document.
        /// </summary>
        /// <param name="sender">The button click sender.</param>
        /// <param name="e">Routed event args.</param>
        private void btnRequestClear_Click(object sender, RoutedEventArgs e)
        {
            txtRequest.Text = string.Empty;
        }

        /// <summary>
        /// Copy the content of the Response TextBox to the Clipboard.
        /// </summary>
        /// <param name="sender">The button that invokes the event.</param>
        /// <param name="e">Event arguments.</param>
        private void btnResponseCopy_Click(object sender, RoutedEventArgs e)
        {
            TerminalWindowHelper.Copy((Image)sender, txtResponse.Markdown);
        }

        #endregion Event Handlers

        #region Methods

        /// <summary>
        /// Starts the control with the given options and package.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="package">The package.</param>
        public void StartControl(OptionPageGridGeneral options, Package package)
        {
            this.options = options;
            this.package = package;

            completionManager = new CompletionManager(package, txtRequest);
        }

        /// <summary>
        /// Handles the result of an operation and appends it to the end of the txtResponse control.
        /// </summary>
        /// <param name="result">The result of the operation.</param>
        private async void ResultHandler(string result)
        {
            if (cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            if (firstIteration)
            {
                EnableDisableButtons(true, true);

                await VS.StatusBar.ShowProgressAsync(Constants.MESSAGE_WAITING_CHATGPT, 2, 2);

                firstIteration = false;
                responseStarted = false;
            }

            if (!options.SingleResponse && !responseStarted && (result.Equals("\n") || result.Equals("\r") || result.Equals(Environment.NewLine)))
            {
                //Do nothing when API send only break lines on response begin
                return;
            }

            responseStarted = true;

            if (removeCodeTagsFromOpenAIResponses)
            {
                result = TextFormat.RemoveCodeTagsFromOpenAIResponses(result);
            }
            else if (options.SingleResponse)
            {
                result = TextFormat.RemoveBlankLinesFromResult(result);
            }

            txtResponse.Markdown = TextFormat.AdjustCodeLanguage(result);
        }

        /// <summary>
        /// Requests to the chatGPT window with the given command and selected text.
        /// </summary>
        /// <param name="command">The command to be sent.</param>
        /// <param name="selectedText">The selected text to be sent.</param>
        public async System.Threading.Tasks.Task RequestToWindowAsync(string command, string selectedText)
        {
            try
            {
                firstIteration = true;

                this.removeCodeTagsFromOpenAIResponses = false;

                await VS.StatusBar.ShowProgressAsync("Requesting API", 1, 2);

                EnableDisableButtons(false, true);

                txtRequest.Text = command + Environment.NewLine + Environment.NewLine + selectedText;

                txtResponse.Markdown = string.Empty;

                cancellationTokenSource = new CancellationTokenSource();

                if (options.SingleResponse || removeCodeTagsFromOpenAIResponses)
                {
                    string result = await ChatGPT.GetResponseAsync(options, command, selectedText, options.StopSequences.Split([','], StringSplitOptions.RemoveEmptyEntries), cancellationTokenSource.Token);

                    ResultHandler(result);
                }
                else
                {
                    await ChatGPT.GetResponseAsync(options, command, selectedText, options.StopSequences.Split([','], StringSplitOptions.RemoveEmptyEntries), ResultHandler, cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                EnableDisableButtons(true, false);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);

                await VS.StatusBar.ShowProgressAsync(ex.Message, 2, 2);

                EnableDisableButtons(true, false);

                MessageBox.Show(ex.Message, Constants.EXTENSION_NAME, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Enables or disables the send button and cancel button based on the provided parameters.
        /// </summary>
        /// <param name="enableSendButton">A boolean value indicating whether to enable or disable the send button.</param>
        /// <param name="enableCancelButton">A boolean value indicating whether to enable or disable the cancel button.</param>
        private async void EnableDisableButtons(bool enableSendButton, bool enableCancelButton)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            grdProgress.Visibility = enableSendButton ? Visibility.Collapsed : Visibility.Visible;

            btnRequestSend.IsEnabled = enableSendButton;
            btnCancel.IsEnabled = enableCancelButton;
        }


        /// <summary>
        /// Recursively searches for all TextEditor controls within the visual tree of a given DependencyObject.
        /// </summary>
        /// <param name="parent">The parent DependencyObject to start the search from.</param>
        /// <returns>
        /// A list of all found TextEditor controls.
        /// </returns>
        public static List<TextEditor> FindMarkDownCodeTextEditors(DependencyObject parent)
        {
            List<TextEditor> foundChildren = [];

            if (parent == null)
            {
                return foundChildren;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                TextEditor childType = child as TextEditor;

                if (childType == null)
                {
                    foundChildren.AddRange(FindMarkDownCodeTextEditors(child));
                }
                else
                {
                    foundChildren.Add(childType);
                }
            }

            return foundChildren;
        }
        #endregion Methods        
    }
}