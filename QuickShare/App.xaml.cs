﻿using Microsoft.QueryStringDotNET;
using Newtonsoft.Json;
using QuickShare.Common;
using QuickShare.DataStore;
using QuickShare.FileTransfer;
using QuickShare.TextTransfer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace QuickShare
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += App_UnhandledException;

            UWP.Rome.RomePackageManager.Instance.Initialize("com.quickshare.service");
            DataStore.DataStorageProviders.Init(Windows.Storage.ApplicationData.Current.LocalFolder.Path);
        }

        private async void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var message = new MessageDialog(e.Message + "\r\n\r\n" + e.Exception.ToString(), "Unhandled exception occured.");
            await message.ShowAsync();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Debug.WriteLine("Launched.");
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = true;
            }
#endif
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e?.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e?.PrelaunchActivated != true)
            {
                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(typeof(MainPage), e?.Arguments);
                }
                ApplicationView.GetForCurrentView().SetPreferredMinSize(new Size(330, 550));

                // Ensure the current window is active
                Window.Current.Activate();
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        protected override async void OnActivated(IActivatedEventArgs e)
        {
            Debug.WriteLine("Activated.");

            Frame rootFrame = Window.Current.Content as Frame;

            bool isJustLaunched = (rootFrame == null);

            if (e is ToastNotificationActivatedEventArgs)
            {
                var toastActivationArgs = e as ToastNotificationActivatedEventArgs;

                // Parse the query string
                QueryString args = QueryString.Parse(toastActivationArgs.Argument);

                HistoryRow hr;
                switch (args["action"])
                {
                    case "fileProgress":
                        launchRootFrameIfNecessary(ref rootFrame);
                        if (rootFrame.Content is MainPage)
                            break;
                        rootFrame.Navigate(typeof(MainPage));
                        break;
                    case "fileFinished":
                        launchRootFrameIfNecessary(ref rootFrame);

                        //TODO: Open history page

                        break;
                    case "openFolder":
                        hr = getHistoryItemGuid(Guid.Parse(args["guid"]));
                        await HelperClasses.LaunchOperations.LaunchFolderFromPathAsync((hr.Data as ReceivedFileCollection).StoreRootPath);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    case "openFolderSingleFile":
                        hr = getHistoryItemGuid(Guid.Parse(args["guid"]));
                        await HelperClasses.LaunchOperations.LaunchFolderFromPathAndSelectSingleItemAsync((hr.Data as ReceivedFileCollection).Files[0].StorePath, (hr.Data as ReceivedFileCollection).Files[0].Name);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    case "openSingleFile":
                        hr = getHistoryItemGuid(Guid.Parse(args["guid"]));
                        await HelperClasses.LaunchOperations.LaunchFileFromPathAsync((hr.Data as ReceivedFileCollection).Files[0].StorePath, (hr.Data as ReceivedFileCollection).Files[0].Name);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    default:
                        break;
                }

            }

            base.OnActivated(e);
        }
        
        private HistoryRow getHistoryItemGuid(Guid guid)
        {
            HistoryRow hr;
            DataStorageProviders.HistoryManager.Open();
            hr = DataStorageProviders.HistoryManager.GetItem(guid);
            DataStorageProviders.HistoryManager.Close();
            return hr;
        }

        private void launchRootFrameIfNecessary(ref Frame rootFrame)
        {
            if (rootFrame == null)
            {
                OnLaunched(null);
                rootFrame = Window.Current.Content as Frame;
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }

        private AppServiceConnection appServiceConnection;
        private BackgroundTaskDeferral appServiceDeferral;

        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);

            IBackgroundTaskInstance taskInstance = args.TaskInstance;
            AppServiceTriggerDetails appService = taskInstance.TriggerDetails as AppServiceTriggerDetails;

            if (appService?.Name == "com.quickshare.notificationservice")
            {
                appServiceDeferral = taskInstance.GetDeferral();
                taskInstance.Canceled += OnAppServicesCanceled;
                appServiceConnection = appService.AppServiceConnection;
                appServiceConnection.RequestReceived += OnAppServiceRequestReceived;
                appServiceConnection.ServiceClosed += AppServiceConnection_ServiceClosed;
            }
        }

        private async void OnAppServiceRequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            AppServiceDeferral messageDeferral = args.GetDeferral();
            ValueSet message = args.Request.Message;

            if (message["Type"].ToString() == "FileTransferProgress")
            {
                await DispatcherEx.RunOnCoreDispatcherIfPossible(async () =>
                {
                    await NotificationHandler.HandleAsync(JsonConvert.DeserializeObject<FileTransferProgressEventArgs>(message["Data"] as string));
                });
            }
            else if (message["Type"].ToString() == "TextReceive")
            {
                await DispatcherEx.RunOnCoreDispatcherIfPossible(async () =>
                {
                    await NotificationHandler.HandleAsync(JsonConvert.DeserializeObject<TextReceiveEventArgs>(message["Data"] as string));
                });
            }

            ValueSet returnMessage = new ValueSet();
            returnMessage.Add("Status", "OK");
            await args.Request.SendResponseAsync(returnMessage);

            messageDeferral.Complete();
        }

        private void OnAppServicesCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            appServiceDeferral.Complete();
        }

        private void AppServiceConnection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            appServiceDeferral.Complete();
        }
    }
}
