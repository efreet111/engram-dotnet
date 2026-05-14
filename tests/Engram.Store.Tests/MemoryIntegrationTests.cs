using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Engram.Store;
using Xunit;

namespace Engram.Store.Tests
{
    public class MemoryIntegrationTests
    {
        private readonly StoreConfig _cfg = StoreConfig.FromEnvironment();

        [Fact]
        public async Task LocalAndRemoteStore_SaveAndRetrieveObservation()
        {
            // Create a unique title to avoid collisions
            var title = $"Test Observation {Guid.NewGuid()}";
            var content = "Integration test content";
            var project = "integration-test";
            var scope = "personal";

            // --- Local SQLite Store ---
            var localStore = new SqliteStore(_cfg);
            
            // We need a valid session ID that exists in the database
            var localSessionId = "local-test-session-" + Guid.NewGuid().ToString().Substring(0, 8);
            await localStore.CreateSessionAsync(localSessionId, project, "/tmp");

            var localParams = new AddObservationParams
            {
                SessionId = localSessionId,
                Type = "test",
                Title = title,
                Content = content,
                Project = project,
                Scope = scope,
            };
            
            var localId = await localStore.AddObservationAsync(localParams);
            var localObs = await localStore.GetObservationAsync(localId);
            
            Assert.NotNull(localObs);
            Assert.Equal(title, localObs.Title);
            Assert.Equal(content, localObs.Content);
            Assert.Equal(project, localObs.Project);
            // SqliteStore.NormalizeScope will turn "personal" into "personal:username" or similar if needed
            // But let's check if it starts with personal
            Assert.StartsWith("personal", localObs.Scope);

            // --- Remote HTTP Store ---
            // If ENGRAM_URL is not set, this might fail or fallback depending on implementation
            // In HttpStore.cs constructor, it uses cfg.RemoteUrl
            if (!string.IsNullOrEmpty(_cfg.RemoteUrl))
            {
                var remoteStore = new HttpStore(_cfg);
                
                var remoteSessionId = "remote-test-session-" + Guid.NewGuid().ToString().Substring(0, 8);
                await remoteStore.CreateSessionAsync(remoteSessionId, project, "/tmp");

                var remoteParams = new AddObservationParams
                {
                    SessionId = remoteSessionId,
                    Type = "test",
                    Title = title,
                    Content = content,
                    Project = project,
                    Scope = scope,
                };
                
                var remoteId = await remoteStore.AddObservationAsync(remoteParams);
                var remoteObs = await remoteStore.GetObservationAsync(remoteId);
                
                Assert.NotNull(remoteObs);
                Assert.Equal(title, remoteObs.Title);
                Assert.Equal(content, remoteObs.Content);
                
                // Cleanup remote
                await remoteStore.DeleteObservationAsync(remoteId);
            }

            // Cleanup local
            await localStore.DeleteObservationAsync(localId);
        }
    }
}
