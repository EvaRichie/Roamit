﻿using Android.Content;
using Android.OS;
using Microsoft.ConnectedDevices;
using QuickShare.Common.Rome;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuickShare.Common;

namespace QuickShare.Droid.RomeComponent
{
    public class RomePackageManager : IRomePackageManager
    {
        readonly string appIdentifier = "36835MahdiGhiasi.Roamit_yddpmccgg2mz2";
        string appServiceName = "";

        AppServiceConnection appService;
        RemoteSystem remoteSystem;
        RemoteSystemConnectionRequest connectionRequest;
        RomeHelper romeHelper = new RomeHelper();
        //AppServiceConnectionListener connectionListener = new AppServiceConnectionListener();
        //AppServiceResponseListener responseListener = new AppServiceResponseListener();

        Context context;

        public ObservableCollection<RemoteSystem> RemoteSystems
        {
            get
            {
                return romeHelper?.RemoteSystems ?? new ObservableCollection<RemoteSystem>();
            }
        }

        public RomePackageManager(Context _context)
        {
            context = _context;
        }

        private async Task<RemoteSystem> RediscoverRemoteSystem(RemoteSystem rs)
        {
            await ReinitializeDiscovery();

            int count = 0;
            RemoteSystem rsNew = null;
            while (rsNew == null)
            {
                rsNew = romeHelper.RemoteSystems.FirstOrDefault(x => x.Id == rs.Id);
                count++;

                if (count > 20)
                    return null;

                await Task.Delay(50);
            }

            return rsNew;
        }


        public async Task<RomeAppServiceConnectionStatus> Connect(object _remoteSystem, bool keepAlive)
        {
            return await Connect(_remoteSystem, keepAlive, null);
        }

        public async Task<RomeAppServiceConnectionStatus> Connect(object _remoteSystem, bool keepAlive, Uri wakeUri)
        {
            var rs = _remoteSystem as RemoteSystem;
            if (rs == null)
                throw new InvalidCastException();

            remoteSystem = rs;

            if ((wakeUri != null) && (!rs.IsAvailableByProximity) && (rs.Kind.Value == RemoteSystemKinds.Phone.Value))
            {
                //Wake device first
                var wakeResult = await LaunchUri(wakeUri, rs);

                if (wakeResult == RomeRemoteLaunchUriStatus.Success)
                {
                    RemoteSystem rsNew = await RediscoverRemoteSystem(rs);
                    System.Diagnostics.Debug.WriteLine(rsNew.IsAvailableByProximity);
                    if (rsNew == null)
                        return RomeAppServiceConnectionStatus.RemoteSystemUnavailable;
                    else
                        rs = rsNew;
                }
            }

            try
            {
                if (sendSemaphore.CurrentCount == 0)
                    sendSemaphore.Release();
            }
            catch
            { }

            connectionRequest = new RemoteSystemConnectionRequest(rs);
            appService = new AppServiceConnection(appServiceName, appIdentifier, connectionRequest);
            var result = await appService.OpenRemoteAsync();

            var finalResult = result.ConvertToRomeAppServiceConnectionStatus();

            if (finalResult == RomeAppServiceConnectionStatus.AppNotInstalled)
                await LaunchStoreForApp(_remoteSystem);

            return finalResult;
        }

        public void CloseAppService()
        {
            appService = null;
        }

        public void Initialize(string _appServiceName)
        {
            appServiceName = _appServiceName;

        }

        public async Task InitializeDiscovery()
        {
            await romeHelper.InitializeAsync(context);
        }

        private async Task ReinitializeDiscovery()
        {
            romeHelper = new RomeHelper();
            await InitializeDiscovery();
        }

        public async Task<RomeRemoteLaunchUriStatus> LaunchStoreForApp()
        {
            return await LaunchUri(new Uri(@"ms-windows-store://pdp/?PFN=" + appIdentifier));
        }

        public async Task<RomeRemoteLaunchUriStatus> LaunchStoreForApp(object rs)
        {
            return await LaunchUri(new Uri(@"ms-windows-store://pdp/?PFN=" + appIdentifier), rs);
        }

        public async Task<RomeRemoteLaunchUriStatus> LaunchUri(Uri uri)
        {
            return await LaunchUri(uri, remoteSystem);
        }

        public async Task<RomeRemoteLaunchUriStatus> LaunchUri(Uri uri, object remoteSystemOverride)
        {
            RemoteSystem rs = null;
            if (remoteSystemOverride != null)
            {
                rs = remoteSystemOverride as RemoteSystem;
                if (rs == null)
                    throw new InvalidCastException();
            }

            var request = new RemoteSystemConnectionRequest(rs);
            var launchUriStatus = await RemoteLauncher.LaunchUriAsync(request, uri);

            var result = launchUriStatus.ConvertToRomeRemoteLaunchUriStatus();
            if (result == RomeRemoteLaunchUriStatus.ProtocolUnavailable)
                await LaunchStoreForApp(rs);

            return result;
        }

        //Instantiate a Singleton of the Semaphore with a value of 1. This means that only 1 thread can be granted access at a time.
        static SemaphoreSlim sendSemaphore = new SemaphoreSlim(1, 1);
        public async Task<RomeAppServiceResponse> Send(Dictionary<string, object> data)
        {
            if (sendSemaphore.CurrentCount == 0)
                Android.Util.Log.Debug("CARRIER_DEBUG", "RomePackageManager.Send() -> Waiting for semaphore to get released...");

            await sendSemaphore.WaitAsync();

            Android.Util.Log.Debug("CARRIER_DEBUG", "RomePackageManager.Send() -> Sending message...");

            Bundle bundle = data.ConvertToBundle();
            var result = await appService.SendMessageAsync(bundle);

            try
            {
                sendSemaphore.Release();
            }
            catch { }

            Android.Util.Log.Debug("CARRIER_DEBUG", "RomePackageManager.Send() -> Response received.");

            return result.ConvertToRomeAppServiceResponse();
        }

        public async Task<bool> QuickClipboard(string _text, RemoteSystem _remoteSystem, string _senderName, string _receiveEndpoint)
        {
            if ((_text + _senderName).Length > 1024)
                return false;

            var uri = new Uri(_receiveEndpoint + ((_receiveEndpoint.Last() == '/') ? "" : "/") + _senderName.EncodeToBase64() + "?" + _text.EncodeToBase64());
            var result = await LaunchUri(uri, _remoteSystem);

            if (result == RomeRemoteLaunchUriStatus.Success)
                return true;

            System.Diagnostics.Debug.WriteLine(result);
            return false;
        }
    }
}
