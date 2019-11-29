﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dotmim.Sync.Batch;
using Dotmim.Sync.Data;
using Dotmim.Sync.Data.Surrogate;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Dotmim.Sync.Web.Client
{
    public class WebClientOrchestrator : IRemoteOrchestrator
    {
        private readonly HttpRequestHandler httpRequestHandler = new HttpRequestHandler();

        public Dictionary<string, string> CustomHeaders => this.httpRequestHandler.CustomHeaders;
        public Dictionary<string, string> ScopeParameters => this.httpRequestHandler.ScopeParameters;


        public SyncOptions Options { get; private set; }
        public SyncSchema Schema { get; private set; }

        /// <summary>
        /// Gets or Sets the provider used in this proxy Orchestrator
        /// Should be null. CoreProvider is only used on the remote side (WebProxyServerProvider)
        /// </summary>
        public CoreProvider Provider { get => null; set => throw new NotSupportedException("Proxy Web does not need any provider. Everything is made on the server side"); }

        /// <summary>
        /// Gets or Sets the service uri to the server side
        /// </summary>
        public Uri ServiceUri
        {
            get => this.httpRequestHandler.BaseUri;
            set => this.httpRequestHandler.BaseUri = value;
        }

        public HttpClientHandler Handler
        {
            get => this.httpRequestHandler.Handler;
            set => this.httpRequestHandler.Handler = value;
        }
        public CookieHeaderValue Cookie
        {
            get => this.httpRequestHandler.Cookie;
            set => this.httpRequestHandler.Cookie = value;
        }

        public void On<T>(Func<T, Task> interceptorFunc) where T : ProgressArgs
        {
            throw new NotSupportedException("Proxy Web does support interceptors, yet.");
        }

        public void On<T>(Action<T> interceptorAction) where T : ProgressArgs
        {
            throw new NotSupportedException("Proxy Web does support interceptors, yet.");
        }

        public void On(Interceptors interceptors)
        {
            throw new NotSupportedException("Proxy Web does support interceptors, yet.");
        }

        public WebClientOrchestrator()
        {

        }
        public WebClientOrchestrator(string serviceUri) : this(new Uri(serviceUri)) { }


        public WebClientOrchestrator(Uri serviceUri)
        {
            this.httpRequestHandler = new HttpRequestHandler(serviceUri, CancellationToken.None);
        }

        /// <summaryWebProxyClientOrchestrator
        /// Use this constructor when you are on the Remote Side, only
        /// </summary>
        public WebClientOrchestrator(Uri serviceUri,
                                      Dictionary<string, string> scopeParameters = null,
                                      Dictionary<string, string> customHeaders = null)
        {
            this.httpRequestHandler = new HttpRequestHandler(serviceUri, CancellationToken.None);

            foreach (var sp in scopeParameters)
                this.AddScopeParameter(sp.Key, sp.Value);

            foreach (var ch in customHeaders)
                this.AddCustomHeader(ch.Key, ch.Value);
        }

        public void AddScopeParameter(string key, string value)
        {
            if (this.httpRequestHandler.ScopeParameters.ContainsKey(key))
                this.httpRequestHandler.ScopeParameters[key] = value;
            else
                this.httpRequestHandler.ScopeParameters.Add(key, value);

        }

        public void AddCustomHeader(string key, string value)
        {
            if (this.httpRequestHandler.CustomHeaders.ContainsKey(key))
                this.httpRequestHandler.CustomHeaders[key] = value;
            else
                this.httpRequestHandler.CustomHeaders.Add(key, value);

        }

        /// <summary>
        /// Send a request to remote web proxy for First step : Ensure scopes and schema
        /// </summary>
        public async Task<(SyncContext context, SyncSchema schema)>
            EnsureSchemaAsync(SyncContext context, SyncSchema schema, SyncOptions options,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            // Create the message to be sent
            var httpMessage = new HttpMessage
            {
                SyncContext = context,
                Step = HttpStep.EnsureScopes,
                Content = new HttpMessageEnsureScopesRequest(schema.ScopeName)
            };

            // Post the request and get the response from server
            var httpMessageResponse = await this.httpRequestHandler.ProcessRequestAsync(httpMessage, context.SessionId,
                    options, cancellationToken).ConfigureAwait(false);

            if (httpMessageResponse == null)
                throw new Exception("Can't have an empty body");

            HttpMessageEnsureScopesResponse ensureScopesResponse;
            if (httpMessageResponse.Content is HttpMessageEnsureScopesResponse)
                ensureScopesResponse = httpMessageResponse.Content as HttpMessageEnsureScopesResponse;
            else
                ensureScopesResponse = (httpMessageResponse.Content as JObject).ToObject<HttpMessageEnsureScopesResponse>();


            if (ensureScopesResponse == null)
                throw new ArgumentException("Http Message content for Ensure scope can't be null");

            if (ensureScopesResponse.Schema == null || ensureScopesResponse.Schema.Set == null || ensureScopesResponse.Schema.Set.Tables.Count <= 0)
                throw new ArgumentException("Schema from EnsureScope can't be null and may contains at least one table");

            this.Options = options;
            this.Schema = ensureScopesResponse.Schema;

            // Return scopes and new shema
            return (httpMessageResponse.SyncContext,
                    ensureScopesResponse.Schema);
        }


        private async Task<(HttpMessageSendChangesResponse, SyncContext)> SendMessageAsync(SyncContext context, HttpMessageSendChangesRequest changesToSend, CancellationToken cancellationToken)
        {
            // Create the message enveloppe
            var httpMessage = new HttpMessage
            {
                Step = HttpStep.SendChanges,
                SyncContext = context,
                Content = changesToSend
            };

            //Post request and get response
            var httpMessageResponse = await this.httpRequestHandler.ProcessRequestAsync(
                httpMessage, context.SessionId, this.Options, cancellationToken).ConfigureAwait(false);

            if (httpMessageResponse == null)
                throw new Exception("Can't have an empty body");

            HttpMessageSendChangesResponse httpMessageContent;

            if (httpMessageResponse.Content is HttpMessageSendChangesResponse)
                httpMessageContent = httpMessageResponse.Content as HttpMessageSendChangesResponse;
            else
                httpMessageContent = (httpMessageResponse.Content as JObject).ToObject<HttpMessageSendChangesResponse>();

            return (httpMessageContent, httpMessageResponse.SyncContext);
        }


        public async Task<(SyncContext, long, BatchInfo, DatabaseChangesSelected)>
            ApplyThenGetChangesAsync(SyncContext context, ScopeInfo scope,
                                     BatchInfo clientBatchInfo, CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            // if we don't have any BatchPartsInfo, just generate a new one to get, at least, something to send to the server
            // and get a response with new data from server
            if (clientBatchInfo == null)
                clientBatchInfo = new BatchInfo(true);

            // --------------------------------------------------------------
            // STEP 1 : Send everything to the server side
            // --------------------------------------------------------------

            // Response from server
            HttpMessage httpMessageResponse = null;

            // response
            HttpMessageSendChangesResponse httpMessageContent = null;


            // If not in memory and BatchPartsInfo.Count == 0, nothing to send.
            // But we need to send something, so generate a little batch part
            if (clientBatchInfo.InMemory || (!clientBatchInfo.InMemory && clientBatchInfo.BatchPartsInfo.Count == 0))
            {
                var changesToSend = new HttpMessageSendChangesRequest(scope);
                changesToSend.Changes = clientBatchInfo.InMemoryData;
                changesToSend.IsLastBatch = true;
                changesToSend.BatchIndex = 0;

                (httpMessageContent, context) = await SendMessageAsync(context, changesToSend, cancellationToken);

            }
            else
            {
                // Foreach part, will have to send them to the remote
                // once finished, return context
                foreach (var bpi in clientBatchInfo.BatchPartsInfo.OrderBy(bpi => bpi.Index))
                {
                    // If BPI is InMempory, no need to deserialize from disk
                    // othewise load it
                    bpi.LoadBatch();

                    var changesToSend = new HttpMessageSendChangesRequest(scope);

                    // Set the change request properties
                    changesToSend.Changes = bpi.Data;
                    changesToSend.IsLastBatch = bpi.IsLastBatch;
                    changesToSend.BatchIndex = bpi.Index;

                    (httpMessageContent, context) = await SendMessageAsync(context, changesToSend, cancellationToken);

                    // for some reasons, if server don't want to wait for more, just break
                    // That should never happened, actually
                    if (httpMessageResponse.Step != HttpStep.SendChangesInProgress)
                        break;

                }

            }

            // --------------------------------------------------------------
            // STEP 2 : Receive everything from the server side
            // --------------------------------------------------------------

            // Now we have sent all the datas to the server and now :
            // We have a FIRST response from the server with new datas 
            // 1) Could be the only one response (enough or InMemory is set on the server side)
            // 2) Could bt the first response and we need to download all batchs

            // While we have an other batch to process
            var isLastBatch = false;

            // Get if we need to work in memory or serialize things
            var workInMemoryLocally = this.Options.BatchSize == 0;

            // Create the BatchInfo and SyncContext to return at the end
            // Set InMemory by default to "true", but the real value is coming from server side
            var serverBatchInfo = new BatchInfo(workInMemoryLocally, this.Options.BatchDirectory);

            // stats
            DatabaseChangesSelected serverChangesSelected = null;

            //timestamp generated by the server, hold in the client db
            long remoteClientTimestamp = 0;

            // While we are not reaching the last batch from server
            do
            {
                // Check if we are at the last batch.
                // If so, we won't make another loop
                isLastBatch = httpMessageContent.IsLastBatch;
                serverChangesSelected = httpMessageContent.ChangesSelected;
                context = httpMessageResponse == null ? context : httpMessageResponse.SyncContext;
                remoteClientTimestamp = httpMessageContent.RemoteClientTimestamp;


                // Create a BatchPartInfo instance
                serverBatchInfo.AddChanges(httpMessageContent.Changes, httpMessageContent.BatchIndex, false);

                // free some memory
                if (!workInMemoryLocally && httpMessageContent.Changes != null)
                    httpMessageContent.Changes.Clear();

                if (!isLastBatch)
                {
                    // Ask for the next batch index
                    var requestBatchIndex = httpMessageContent.BatchIndex + 1;

                    // Create the message enveloppe
                    var httpMessage = new HttpMessage
                    {
                        Step = HttpStep.GetChanges,
                        SyncContext = context,
                    };

                    // Maybe miss some info here
                    httpMessage.Content = new HttpMessageGetMoreChangesRequest
                    {
                        BatchIndexRequested = requestBatchIndex,
                    };

                    httpMessageResponse = await this.httpRequestHandler.ProcessRequestAsync(
                                httpMessage, context.SessionId, this.Options, cancellationToken).ConfigureAwait(false);

                    if (httpMessageResponse == null)
                        throw new Exception("Can't have an empty body");

                    if (httpMessageResponse.Content is HttpMessageSendChangesResponse)
                        httpMessageContent = httpMessageResponse.Content as HttpMessageSendChangesResponse;
                    else
                        httpMessageContent = (httpMessageResponse.Content as JObject).ToObject<HttpMessageSendChangesResponse>();
                }


            } while (!isLastBatch);


            return (context, remoteClientTimestamp, serverBatchInfo, serverChangesSelected);
        }

        

    }
}
