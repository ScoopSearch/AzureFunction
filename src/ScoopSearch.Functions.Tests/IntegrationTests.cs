using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Search;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScoopSearch.Functions.Configuration;
using ScoopSearch.Functions.Data;
using ScoopSearch.Functions.Function;
using ScoopSearch.Functions.Indexer;
using Xunit;
using Xunit.Abstractions;
using ScoopSearch.Functions.Manifest;

namespace ScoopSearch.Functions.Tests
{
    public class IntegrationTests
    {
        private readonly IServiceProvider _serviceProvider;

        public IntegrationTests(ITestOutputHelper testOutputHelper)
        {
            var host = CreateHost(testOutputHelper);
            _serviceProvider = host.Services;

            DeleteExistingIndex();
        }

        [Fact]
        public async Task ManualRun()
        {
            var collector = await RunDispatchBucketsCrawlerAsync();

            var bucketCrawler = new BucketCrawler(
                _serviceProvider.GetService<IManifestCrawler>(),
                _serviceProvider.GetService<IIndexer>());

            var maxThreads = Environment.ProcessorCount;
            var tasks = new List<Task>();
            for(int idx = 0; idx < maxThreads; idx++)
            {
                tasks.Add(CreateBucketCrawlerTask(bucketCrawler, collector, idx));
            }

            await Task.WhenAll(tasks);
        }

        private Task CreateBucketCrawlerTask(BucketCrawler instance, Collector<QueueItem> collector, int taskIdx)
        {
            return Task.Run(async () =>
            {
                while (collector.TryDequeue(out var queueItem))
                {
                    await instance.Run(
                        queueItem,
                        _serviceProvider.GetService<ILoggerFactory>().CreateLogger($"BucketCrawler{taskIdx}"),
                        CancellationToken.None);
                }
            });
        }

        private async Task<Collector<QueueItem>> RunDispatchBucketsCrawlerAsync()
        {
            var collector = new Collector<QueueItem>();

            var dispatchBucketsCrawler = new DispatchBucketsCrawler(
                _serviceProvider.GetService<IHttpClientFactory>(),
                _serviceProvider.GetService<IIndexer>(),
                _serviceProvider.GetService<IOptions<BucketsOptions>>());
            await dispatchBucketsCrawler.Run(
                null,
                collector,
                _serviceProvider.GetService<ILoggerFactory>().CreateLogger<DispatchBucketsCrawler>(),
                CancellationToken.None);

            return collector;
        }

        private class Collector<T> : IAsyncCollector<T>
        {
            private readonly ConcurrentQueue<T> _items = new ConcurrentQueue<T>();

            public Task AddAsync(T item, CancellationToken cancellationToken = new CancellationToken())
            {
                _items.Enqueue(item);
                return Task.CompletedTask;
            }

            public bool TryDequeue(out T item)
            {
                return _items.TryDequeue(out item);
            }

            public Task FlushAsync(CancellationToken cancellationToken = new CancellationToken()) => throw new NotImplementedException();
        }

        private IHost CreateHost(ITestOutputHelper testOutputHelper)
        {
            var executionContextOptions = ServiceDescriptor.Singleton<IOptions<ExecutionContextOptions>>(
                serviceProvider =>
                    new OptionsWrapper<ExecutionContextOptions>(
                        new ExecutionContextOptions
                        {
                            AppDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                        }));

            var host = new HostBuilder()
                .ConfigureWebJobs(builder => builder
                    .UseWebJobsStartup<Startup>())
                .ConfigureServices(services => services
                    .Replace(executionContextOptions))
                .ConfigureAppConfiguration((context, config) =>
                    config.AddJsonFile("appsettings.local.json", true))
                .ConfigureLogging((context, builder) =>
                {
                    builder.AddConfiguration(context.Configuration.GetSection("Logging"));
                    builder.AddProvider(new XUnitLoggerProvider(testOutputHelper));
                    builder.AddDebug();
                })
                .Build();

            return host;
        }

        private void DeleteExistingIndex()
        {
            var options = _serviceProvider.GetService<IOptions<AzureSearchOptions>>();
            var client = new SearchServiceClient(options.Value.ServiceName, new SearchCredentials(options.Value.AdminApiKey));
            client.Indexes.Delete(options.Value.IndexName);
        }
    }
}
