﻿using JeffPires.VisualChatGPTStudio.Options;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JeffPires.VisualChatGPTStudio.Utils
{
    /// <summary>
    /// Static class containing methods for interacting with the ChatGPT API.
    /// </summary>
    static class ChatGPT
    {
        private static OpenAIAPI api;
        private static OpenAIAPI apiForAzureTurboChat;
        private static ChatGPTHttpClientFactory chatGPTHttpClient;
        private static readonly TimeSpan timeout = new(0, 0, 120);

        /// <summary>
        /// Asynchronously gets a response from a chatbot.
        /// </summary>
        /// <param name="options">The options for the chatbot.</param>
        /// <param name="systemMessage">The system message to send to the chatbot.</param>
        /// <param name="userInput">The user input to send to the chatbot.</param>
        /// <param name="stopSequences">The stop sequences to use for ending the conversation.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <returns>The response from the chatbot.</returns>
        public static async Task<string> GetResponseAsync(OptionPageGridGeneral options, string systemMessage, string userInput, string[] stopSequences, CancellationToken cancellationToken)
        {
            Conversation chat = CreateConversationForCompletions(options, systemMessage, userInput, stopSequences);

            Task<string> task = chat.GetResponseFromChatbotAsync();

            await Task.WhenAny(task, Task.Delay(timeout, cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            return await task;
        }

        /// <summary>
        /// Asynchronously gets the response from the chatbot.
        /// </summary>
        /// <param name="options">The options for the chat.</param>
        /// <param name="systemMessage">The system message to display in the chat.</param>
        /// <param name="userInput">The user input in the chat.</param>
        /// <param name="stopSequences">The stop sequences to end the conversation.</param>
        /// <param name="resultHandler">The action to handle the chatbot response.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task GetResponseAsync(OptionPageGridGeneral options, string systemMessage, string userInput, string[] stopSequences, Action<string> resultHandler, CancellationToken cancellationToken)
        {
            Conversation chat = CreateConversationForCompletions(options, systemMessage, userInput, stopSequences);

            Task task = chat.StreamResponseFromChatbotAsync(resultHandler);

            await Task.WhenAny(task, Task.Delay(timeout, cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Creates a conversation with the specified options and system message.
        /// </summary>
        /// <param name="options">The options to use for the conversation.</param>
        /// <param name="systemMessage">The system message to append to the conversation.</param>
        /// <returns>The created conversation.</returns>
        public static Conversation CreateConversation(OptionPageGridGeneral options, string systemMessage)
        {
            Conversation chat;

            if (options.Service == OpenAIService.OpenAI || string.IsNullOrWhiteSpace(options.AzureDeploymentId))
            {
                CreateApiHandler(options);

                chat = api.Chat.CreateConversation();
            }
            else
            {
                CreateApiHandlerForAzureTurboChat(options);

                chat = apiForAzureTurboChat.Chat.CreateConversation();
            }

            chat.AppendSystemMessage(systemMessage);

            chat.RequestParameters.Temperature = options.Temperature;
            chat.RequestParameters.MaxTokens = options.MaxTokens;
            chat.RequestParameters.TopP = options.TopP;
            chat.RequestParameters.FrequencyPenalty = options.FrequencyPenalty;
            chat.RequestParameters.PresencePenalty = options.PresencePenalty;

            if (options.Model == ModelLanguageEnum.GPT_4)
            {
                chat.Model = Model.GPT4;
            }
            else if (options.Model == ModelLanguageEnum.GPT_3_5_Turbo_16k)
            {
                chat.Model = "gpt-3.5-turbo-16k";
            }
            else
            {
                chat.Model = Model.ChatGPTTurbo;
            }

            return chat;
        }

        /// <summary>
        /// Creates a conversation for Completions with the given parameters.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="systemMessage">The system message.</param>
        /// <param name="userInput">The user input.</param>
        /// <param name="stopSequences">The stop sequences.</param>
        /// <returns>
        /// The created conversation.
        /// </returns>
        private static Conversation CreateConversationForCompletions(OptionPageGridGeneral options, string systemMessage, string userInput, string[] stopSequences)
        {
            Conversation chat = CreateConversation(options, systemMessage);

            if (options.MinifyRequests)
            {
                userInput = TextFormat.MinifyText(userInput);
            }

            userInput = TextFormat.RemoveCharactersFromText(userInput, options.CharactersToRemoveFromRequests.Split(','));

            chat.AppendUserInput(userInput);

            if (stopSequences != null && stopSequences.Length > 0)
            {
                chat.RequestParameters.MultipleStopSequences = stopSequences;
            }

            return chat;
        }

        /// <summary>
        /// Creates an API handler with the given API key and proxy.
        /// </summary>
        /// <param name="options">All configurations to create the connection</param>
        private static void CreateApiHandler(OptionPageGridGeneral options)
        {
            if (api == null)
            {
                chatGPTHttpClient = new();

                if (!string.IsNullOrWhiteSpace(options.Proxy))
                {
                    chatGPTHttpClient.SetProxy(options.Proxy);
                }

                if (options.Service == OpenAIService.AzureOpenAI)
                {
                    api = OpenAIAPI.ForAzure(options.AzureResourceName, options.AzureDeploymentId, options.ApiKey);
                }
                else
                {
                    APIAuthentication auth;

                    if (!string.IsNullOrWhiteSpace(options.OpenAIOrganization))
                    {
                        auth = new(options.ApiKey, options.OpenAIOrganization);
                    }
                    else
                    {
                        auth = new(options.ApiKey);
                    }

                    api = new(auth);

                    if (!string.IsNullOrWhiteSpace(options.BaseAPI))
                    {
                        api.ApiUrlFormat = options.BaseAPI + "/{0}/{1}";
                    }
                }

                api.HttpClientFactory = chatGPTHttpClient;
            }
            else if ((options.Service == OpenAIService.AzureOpenAI && !api.ApiUrlFormat.ToUpper().Contains("AZURE")) || (options.Service == OpenAIService.OpenAI && api.ApiUrlFormat.ToUpper().Contains("AZURE")))
            {
                api = null;
                CreateApiHandler(options);
            }
            else if (api.Auth.ApiKey != options.ApiKey)
            {
                api.Auth.ApiKey = options.ApiKey;
            }
        }

        /// <summary>
        /// Creates an API handler for Azure TurboChat using the provided options.
        /// </summary>
        /// <param name="options">The options to use for creating the API handler.</param>
        private static void CreateApiHandlerForAzureTurboChat(OptionPageGridGeneral options)
        {
            if (apiForAzureTurboChat == null)
            {
                chatGPTHttpClient = new();

                if (!string.IsNullOrWhiteSpace(options.Proxy))
                {
                    chatGPTHttpClient.SetProxy(options.Proxy);
                }

                apiForAzureTurboChat = OpenAIAPI.ForAzure(options.AzureResourceName, options.AzureDeploymentId, options.ApiKey);

                apiForAzureTurboChat.HttpClientFactory = chatGPTHttpClient;

                if (!string.IsNullOrWhiteSpace(options.AzureApiVersion))
                {
                    apiForAzureTurboChat.ApiVersion = options.AzureApiVersion;
                }
            }
            else if (apiForAzureTurboChat.Auth.ApiKey != options.ApiKey)
            {
                apiForAzureTurboChat.Auth.ApiKey = options.ApiKey;
            }
        }
    }
}
