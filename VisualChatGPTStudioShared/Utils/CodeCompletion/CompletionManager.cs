﻿using Community.VisualStudio.Toolkit;
using EnvDTE;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using JeffPires.VisualChatGPTStudio.Options.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace JeffPires.VisualChatGPTStudio.Utils.CodeCompletion
{
    /// <summary>
    /// Provides functionality for managing and processing completions in the text editors.
    /// </summary>
    public class CompletionManager
    {
        private readonly Package package;
        private readonly TextEditor editor;
        private CompletionWindow completionWindowForCommands;
        private CompletionWindow completionWindowForFiles;
        private readonly List<string> validProjectItems;
        private readonly List<string> validProjectTypes;
        private Timer completionDataFilesAndMethodsUpdateTimer;
        private IList<ICompletionData> completionDataCommands;
        private IList<ICompletionData> completionDataFilesAndMethods;

        private OptionCommands OptionsCommands
        {
            get
            {
                return ((VisuallChatGPTStudioPackage)package).OptionsCommands;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompletionManager"/> class.
        /// </summary>
        /// <param name="package">The package associated with the completion manager.</param>
        /// <param name="editor">The text editor where syntax highlighting will be applied.</param>
        public CompletionManager(Package package, TextEditor editor)
        {
            this.package = package;
            this.editor = editor;

            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("JeffPires.VisualChatGPTStudio.Resources.Highlighting.xshd"))
            {
                using (StreamReader s = new(stream))
                {
                    using (System.Xml.XmlTextReader reader = new(s))
                    {
                        HighlightingManager.Instance.RegisterHighlighting("Completion", [], HighlightingLoader.Load(reader, HighlightingManager.Instance));
                    }
                }
            }

            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Completion");

            validProjectItems =
            [
                ".config",
                ".cs",
                ".css",
                ".html",
                ".js",
                ".json",
                ".md",
                ".sql",
                ".ts",
                ".vb",
                ".xml",
                ".xaml"
            ];

            validProjectTypes =
            [
                ".csproj",
                ".vbproj",
                ".vcxproj",
                ".fsproj",
                ".pyproj",
                ".jsproj",
                ".sqlproj",
                ".wixproj",
                ".njsproj",
                ".shproj"
            ];

            FillDataCommands();

            RegisterDataFilesAndMethodsUpdateEvents();
        }

        #region Public Methods

        /// <summary>
        /// Handles the text entered event, triggering specific actions based on the input text.
        /// If the input is a forward slash ("/"), it calls the ShowCommands method.
        /// If the input is a hash ("#"), it calls the ShowFiles method.
        /// </summary>
        /// <param name="e">The event arguments containing the text input.</param>
        public async System.Threading.Tasks.Task HandleTextEnteredAsync(TextCompositionEventArgs e)
        {
            if (e.Text == "/")
            {
                ShowCommands();
            }
            else if (e.Text == "#")
            {
                await ShowFilesAsync();
            }
        }

        /// <summary>
        /// Handles the TextEntering event for the txtRequest control.
        /// Inserts the currently selected completion item if a non-letter or non-digit character is typed 
        /// while the completion window is open.
        /// </summary>
        /// <param name="e">The event arguments containing the text composition information.</param>
        public void HandleTextEntering(TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && !char.IsLetterOrDigit(e.Text[0]))
            {
                if (completionWindowForCommands != null && completionWindowForCommands.IsActive)
                {
                    completionWindowForCommands.CompletionList.RequestInsertion(e);
                }

                if (completionWindowForFiles != null && completionWindowForFiles.IsActive)
                {
                    completionWindowForFiles.CompletionList.RequestInsertion(e);
                }
            }
        }

        /// <summary>
        /// Replaces the autocomplete placeholders with their corresponding commands.
        /// </summary>
        /// <param name="request">The input string containing the placeholders.</param>
        /// <returns>The modified string with placeholders replaced by their commands.</returns>
        public async Task<string> ReplaceReferencesAsync(string request)
        {
            request = ReplaceCommands(request);

            return await ReplaceFilesAndMethodsAsync(request);
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Fills the completion data commands with predefined command options and their associated resources.
        /// </summary>
        private void FillDataCommands()
        {
            completionDataCommands = [];

            completionDataCommands.Add(new CompletionData("Complete", OptionsCommands.GetCommandAsync(CommandsType.Complete).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/complete.png"));
            completionDataCommands.Add(new CompletionData("Add_Tests", OptionsCommands.GetCommandAsync(CommandsType.AddTests).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/addTests.png"));
            completionDataCommands.Add(new CompletionData("Find_Bugs", OptionsCommands.GetCommandAsync(CommandsType.FindBugs).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/findBugs.png"));
            completionDataCommands.Add(new CompletionData("Optimize", OptionsCommands.GetCommandAsync(CommandsType.Optimize).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/optimize.png"));
            completionDataCommands.Add(new CompletionData("Explain", OptionsCommands.GetCommandAsync(CommandsType.Explain).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/explain.png"));
            completionDataCommands.Add(new CompletionData("Add_Comments", OptionsCommands.GetCommandAsync(CommandsType.AddCommentsForLines).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/addComments.png"));
            completionDataCommands.Add(new CompletionData("Add_Summary", OptionsCommands.GetCommandAsync(CommandsType.AddSummary).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/addSummary.png"));
            completionDataCommands.Add(new CompletionData("Translate", OptionsCommands.GetCommandAsync(CommandsType.Translate).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/translate.png"));
            completionDataCommands.Add(new CompletionData("Custom_Before", OptionsCommands.GetCommandAsync(CommandsType.CustomBefore).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/customBefore.png"));
            completionDataCommands.Add(new CompletionData("Custom_After", OptionsCommands.GetCommandAsync(CommandsType.CustomAfter).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/customAfter.png"));
            completionDataCommands.Add(new CompletionData("Custom_Replace", OptionsCommands.GetCommandAsync(CommandsType.CustomReplace).Result, "pack://application:,,,/VisualChatGPTStudio;component/Resources/customReplace.png"));
        }

        /// <summary>
        /// Registers a timer to periodically update data files and methods every 10 seconds.
        /// It also ensures that the initial update is performed on the main thread if the solution is open.
        /// </summary>
        private void RegisterDataFilesAndMethodsUpdateEvents()
        {
            completionDataFilesAndMethodsUpdateTimer = new Timer(10000);
            completionDataFilesAndMethodsUpdateTimer.Elapsed += async (sender, e) => System.Threading.Tasks.Task.Run(async () => await FillDataFilesAndMethodsAsync());
            completionDataFilesAndMethodsUpdateTimer.AutoReset = true;
            completionDataFilesAndMethodsUpdateTimer.Enabled = true;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                DTE dte = await VS.GetServiceAsync<DTE, DTE>();

                if (dte.Solution.IsOpen)
                {
                    await FillDataFilesAndMethodsAsync();
                }
            });
        }

        /// <summary>
        /// Asynchronously fills the data files and methods.
        /// </summary>
        private async System.Threading.Tasks.Task FillDataFilesAndMethodsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DTE dte = await VS.GetServiceAsync<DTE, DTE>();

            if (!dte.Solution.IsOpen)
            {
                return;
            }

            if (completionDataFilesAndMethodsUpdateTimer.Interval != 120000)
            {
                completionDataFilesAndMethodsUpdateTimer.Interval = 120000;
            }

            completionDataFilesAndMethods = [];

            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                string projectName = project.Name;

                await PopulateProjectItemsForCompletionAsync(project.ProjectItems, Path.GetFileName(dte.Solution.FileName), projectName);
            }
        }

        /// <summary>
        /// Replaces command titles placeholders in the given request string with their corresponding commands.
        /// </summary>
        /// <param name="request">The input string containing command placeholders.</param>
        /// <returns>The modified string with command titles placeholders replaced by their commands.</returns>
        private string ReplaceCommands(string request)
        {
            foreach (ICompletionData completionDataCommand in completionDataCommands)
            {
                if (request.Contains("/" + completionDataCommand.Text))
                {
                    request = request.Replace("/" + completionDataCommand.Text, completionDataCommand.Description.ToString());
                }
            }

            return request;
        }

        /// <summary>
        /// Replaces placeholders in the request string with the corresponding file contents or method definitions 
        /// based on the completion data available. It searches for placeholders prefixed with '#' and replaces 
        /// them with the actual content retrieved from the specified file or method.
        /// </summary>
        /// <param name="request">The input string containing placeholders to be replaced.</param>
        /// <returns>A task that represents the asynchronous operation, containing the modified request string 
        /// with placeholders replaced by their corresponding content.</returns>
        private async System.Threading.Tasks.Task<string> ReplaceFilesAndMethodsAsync(string request)
        {
            if (completionDataFilesAndMethods == null)
            {
                return request;
            }

            foreach (CompletionData completionData in completionDataFilesAndMethods)
            {
                if (!request.Contains("#" + completionData.Text))
                {
                    continue;
                }

                string filePath = completionData.Aux.ToString();

                string content;

                if (string.IsNullOrWhiteSpace(Path.GetExtension(completionData.Text)))
                {
                    content = await GetMethodContentAsync(filePath, completionData.Text);
                }
                else
                {
                    content = File.ReadAllText(filePath);
                }

                request = request.Replace("#" + completionData.Text, content);
            }

            return request;
        }

        /// <summary>
        /// Displays a completion window for available commands in the editor, populating it with various command options 
        /// such as "Complete", "Add Tests", "Find Bugs", and others, each associated with an icon and retrieved asynchronously.
        /// </summary>
        private void ShowCommands()
        {
            completionWindowForCommands = new CompletionWindow(editor.TextArea)
            {
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                MinHeight = 45,
                MinWidth = 130
            };

            completionWindowForCommands.Closed += delegate { completionWindowForCommands = null; };

            foreach (ICompletionData completionDataCommand in completionDataCommands)
            {
                completionWindowForCommands.CompletionList.CompletionData.Add(completionDataCommand);
            }

            completionWindowForCommands.Show();
        }

        /// <summary>
        /// Asynchronously displays a completion window for files in the current solution.
        /// </summary>
        private async System.Threading.Tasks.Task ShowFilesAsync()
        {
            if (completionDataFilesAndMethods == null || completionDataFilesAndMethods.Count == 0)
            {
                return;
            }

            completionWindowForFiles = new CompletionWindow(editor.TextArea)
            {
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                MinHeight = 45,
                MinWidth = 130
            };

            completionWindowForFiles.Closed += delegate { completionWindowForFiles = null; };

            foreach (ICompletionData data in completionDataFilesAndMethods)
            {
                completionWindowForFiles.CompletionList.CompletionData.Add(data);
            }

            completionWindowForFiles.Show();
        }

        /// <summary>
        /// Asynchronously populates a list of completion data for project items in a solution.
        /// This method iterates through the provided project items, checking their types and 
        /// adding valid files to the completion data list. It also recursively processes folders 
        /// and sub-projects to gather all relevant items.
        /// </summary>
        /// <param name="items">The project items to populate from.</param>
        /// <param name="solutionName">The name of the solution containing the project.</param>
        /// <param name="projectName">The name of the project being processed.</param>
        private async System.Threading.Tasks.Task PopulateProjectItemsForCompletionAsync(ProjectItems items, string solutionName, string projectName)
        {
            if (items == null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            foreach (ProjectItem item in items)
            {
                if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                {
                    string fileName = item.Name;
                    string filePath = item.FileNames[1];
                    string fileExtension = Path.GetExtension(fileName);

                    if (validProjectItems.Any(ext => ext.Equals(fileExtension, StringComparison.OrdinalIgnoreCase)))
                    {
                        completionDataFilesAndMethods.Add(new CompletionData(string.Concat(projectName, ".", fileName), filePath, GetFileIcon(fileName), filePath));

                        if (fileExtension.Equals(".cs"))
                        {
                            await PopulateMethodsForCompletionAsync(item, solutionName, projectName);
                        }
                    }
                }
                else if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder || item.Kind == EnvDTE.Constants.vsProjectItemKindVirtualFolder)
                {
                    await PopulateProjectItemsForCompletionAsync(item.ProjectItems, solutionName, projectName);
                }
                else if (item.SubProject != null)
                {
                    await PopulateProjectItemsForCompletionAsync(item.SubProject.ProjectItems, solutionName, projectName);
                }
            }
        }

        /// <summary>
        /// Populates a list of completion data for methods found in the specified project item.
        /// This method reads the file associated with the project item, parses its syntax tree,
        /// and extracts method declarations from class declarations, adding them to the provided
        /// completion data list with a formatted completion text.
        /// </summary>
        /// <param name="item">The project item containing the file to be parsed.</param>
        /// <param name="solutionName">The name of the solution containing the project.</param>
        /// <param name="projectName">The name of the project from which the item originates.</param>
        private async System.Threading.Tasks.Task PopulateMethodsForCompletionAsync(ProjectItem item, string solutionName, string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            IEnumerable<ClassDeclarationSyntax> classDeclarations = await GetClassDeclarationsAsync(item.FileNames[1]);

            foreach (ClassDeclarationSyntax classDecl in classDeclarations)
            {
                string className = classDecl.Identifier.Text;

                IEnumerable<SyntaxNode> declarations = classDecl.DescendantNodes()
                                                                .OfType<SyntaxNode>()
                                                                .Where(node => node is MethodDeclarationSyntax ||
                                                                               node is ConstructorDeclarationSyntax ||
                                                                               node is PropertyDeclarationSyntax ||
                                                                               node is EnumDeclarationSyntax ||
                                                                               node is DelegateDeclarationSyntax ||
                                                                               node is EventDeclarationSyntax);

                foreach (SyntaxNode declaration in declarations)
                {
                    string name = GetSyntaxIdentifier(declaration);

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        string description = $"{solutionName}.{projectName}.{className}.{name}";

                        completionDataFilesAndMethods.Add(new CompletionData(name, description, GetFileIcon("method"), item.FileNames[1]));
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously retrieves class declarations from a specified C# file.
        /// </summary>
        /// <param name="filePath">The path to the C# file from which to extract class declarations.</param>
        /// <returns>A task that represents the asynchronous operation, containing a collection of class declarations.</returns>
        private static async Task<IEnumerable<ClassDeclarationSyntax>> GetClassDeclarationsAsync(string filePath)
        {
            Microsoft.CodeAnalysis.SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath));

            Microsoft.CodeAnalysis.SyntaxNode root = await syntaxTree.GetRootAsync();

            return root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        }

        /// <summary>
        /// Retrieves the icon image source for a given file based on its extension.
        /// </summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>The image source of the file icon.</returns>
        private ImageSource GetFileIcon(string fileName)
        {
            BitmapImage imageSource = new();
            string fileExtension = string.Empty;

            try
            {
                fileExtension = Path.GetExtension(fileName);
            }
            catch (Exception)
            {

            }

            if (fileName == "method")
            {
                fileExtension = "json";
            }
            else if (string.IsNullOrWhiteSpace(fileExtension))
            {
                fileExtension = "folder";
            }
            else if (fileExtension == ".sln")
            {
                fileExtension = "sln";
            }
            else if (validProjectTypes.Any(i => i == fileExtension))
            {
                fileExtension = "vs";
            }
            else if (!validProjectItems.Any(i => i == fileExtension))
            {
                return imageSource;
            }

            string uriSource = $"pack://application:,,,/VisualChatGPTStudio;component/Resources/FileTypes/{fileExtension.Replace(".", string.Empty)}.png";

            imageSource.BeginInit();
            imageSource.UriSource = new Uri(uriSource);
            imageSource.EndInit();

            return imageSource;
        }

        /// <summary>
        /// Asynchronously retrieves the content of a specified method from a given C# file.
        /// Searches through class declarations in the file to find the method by name.
        /// </summary>
        /// <param name="filePath">The path to the C# file from which to extract the method content.</param>
        /// <param name="methodName">The name of the method to retrieve.</param>
        /// <returns>A task that represents the asynchronous operation, containing the full string representation of the method, or an empty string if not found.</returns>
        private async Task<string> GetMethodContentAsync(string filePath, string methodName)
        {
            IEnumerable<ClassDeclarationSyntax> classDeclarations = await GetClassDeclarationsAsync(filePath);

            foreach (ClassDeclarationSyntax classDecl in classDeclarations)
            {
                SyntaxNode methodDecl = classDecl.Members.OfType<SyntaxNode>().FirstOrDefault(m => GetSyntaxIdentifier(m).Equals(methodName, StringComparison.OrdinalIgnoreCase));

                if (methodDecl != null)
                {
                    return methodDecl.ToFullString();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Retrieves the identifier text from a given syntax node, which can be a method, constructor, property, enum, delegate, or event declaration.
        /// </summary>
        /// <param name="declaration">The syntax node from which to extract the identifier.</param>
        /// <returns>
        /// The identifier text of the specified syntax node, or an empty string if the node type is not recognized.
        /// </returns>
        private static string GetSyntaxIdentifier(SyntaxNode declaration)
        {
            return declaration switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.Text,
                PropertyDeclarationSyntax property => property.Identifier.Text,
                EnumDeclarationSyntax enumDecl => enumDecl.Identifier.Text,
                DelegateDeclarationSyntax delegateDecl => delegateDecl.Identifier.Text,
                EventDeclarationSyntax eventDecl => eventDecl.Identifier.Text,
                _ => string.Empty
            };
        }

        #endregion Private Methods
    }
}
