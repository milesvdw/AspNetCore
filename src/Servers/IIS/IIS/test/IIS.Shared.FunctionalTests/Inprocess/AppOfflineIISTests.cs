// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.IIS.FunctionalTests.Utilities;
using Microsoft.AspNetCore.Server.IISIntegration.FunctionalTests;
using Microsoft.AspNetCore.Server.IntegrationTesting;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace IISExpress.FunctionalTests.Inprocess
{
    public class AppOfflineIISTests : IISFunctionalTestBase
    {
        public AppOfflineIISTests(PublishedSitesFixture fixture) : base(fixture)
        {
        }

        [ConditionalFact]
        [RequiresNewHandler]
        public async Task AppOfflineDroppedWhileSiteStarting_SiteShutsDown_InProcess()
        {
            // This test often hits a race between debug logging and stdout redirection closing the handle
            // we are fine having this race
            using (AppVerifier.Disable(DeployerSelector.ServerType, 0x300))
            {
                var deploymentResult = await DeployApp(HostingModel.InProcess);

                for (int i = 0; i < 10; i++)
                {
                    // send first request and add app_offline while app is starting
                    var runningTask = AssertAppOffline(deploymentResult);

                    // This test tries to hit a race where we drop app_offline file while
                    // in process application is starting, application start takes at least 400ms
                    // so we back off for 100ms to allow request to reach request handler
                    // Test itself is racy and can result in two scenarios
                    //    1. ANCM detects app_offline before it starts the request - if AssertAppOffline succeeds we've hit it
                    //    2. Intended scenario where app starts and then shuts down
                    // In first case we remove app_offline and try again
                    await Task.Delay(RetryDelay);

                    AddAppOffline(deploymentResult.ContentRoot);

                    await runningTask.DefaultTimeout();

                    // if AssertAppOffline succeeded ANCM have picked up app_offline before starting the app
                    // try again
                    RemoveAppOffline(deploymentResult.ContentRoot);
                 
                    // Worker process may not have stopped if app offline is dropped first before starting
                    // We can send a reqeust and check if the server is up, and if so add and and remove app offline again
                    // to shut the site down.
                    var response = await deploymentResult.HttpClient.GetAsync("/HelloWorld");
                    if (response.IsSuccessStatusCode)
                    {
                        // Site wasn't shut down, retry.
                        AddAppOffline(deploymentResult.ContentRoot);
                        RemoveAppOffline(deploymentResult.ContentRoot);
                        continue;
                    }

                    deploymentResult.AssertWorkerProcessStop();
                    return;
                }

                Assert.True(false);
            }
        }
    }
}
