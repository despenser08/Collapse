﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;

using Hi3Helper;
using Hi3Helper.Data;
using Hi3Helper.Shared.ClassStruct;

using static Hi3Helper.Logger;
using static Hi3Helper.Data.ConverterTool;
using static Hi3Helper.Shared.Region.LauncherConfig;

namespace CollapseLauncher.Pages
{
    public sealed partial class CachesPage : Page
    {
        int DownloadThread = GetAppConfigValue("DownloadThread").ToInt();
        private async Task DoCachesUpdate()
        {
            await Task.Run(() =>
            {
                try
                {
                    cachesRead = 0;
                    cachesCount = 0;
                    cancellationTokenSource = new CancellationTokenSource();

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateCachesBtn.IsEnabled = false;
                        CancelBtn.IsEnabled = true;
                    });
                    DownloadCachesUpdate();
                    CacheReindexing();

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CachesStatus.Text = "Caches Update Completed!";
                        CachesTotalStatus.Text = "None";
                        CachesTotalProgressBar.Value = 0;
                        UpdateCachesBtn.IsEnabled = true;
                        CheckUpdateBtn.IsEnabled = true;
                        UpdateCachesBtn.Visibility = Visibility.Collapsed;
                        CheckUpdateBtn.Visibility = Visibility.Visible;
                        CancelBtn.IsEnabled = false;
                    });
                }
                catch (OperationCanceledException)
                {
                    LogWriteLine("Caches Update check cancelled!", LogType.Warning);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CachesStatus.Text = $"Operation has been cancelled!";
                        CachesTotalStatus.Text = "None";
                        CachesTotalProgressBar.Value = 0;
                        CheckUpdateBtn.Visibility = Visibility.Visible;
                        CheckUpdateBtn.IsEnabled = true;
                        UpdateCachesBtn.Visibility = Visibility.Collapsed;
                        CancelBtn.IsEnabled = false;
                    });
                    httpClient.ProgressChanged -= CachesDownloadProgress;
                    httpClient.PartialProgressChanged -= CachesPartialDownloadProgress;
                }
                catch (Exception ex)
                {
                    ErrorSender.SendException(ex);
                }
            });
        }

        private void DownloadCachesUpdate()
        {
            string cachesPathType;
            foreach (DataProperties dataType in brokenCachesList)
            {
                switch (dataType.DataType)
                {
                    case CachesType.Data:
                        cachesPathType = Path.Combine(cachesBasePath, "Data");
                        break;
                    default:
                        cachesPathType = Path.Combine(cachesBasePath, "Resources");
                        break;
                }

                cachesEndpointURL = String.Format(CurrentRegion.CachesEndpointURL, dataType.DataType.ToString().ToLower());
                LogWriteLine($"Downloading Cache Type {dataType.DataType} with endpoint: {cachesEndpointURL}", LogType.Default, true);
                foreach (DataPropertiesContent content in dataType.Content)
                {
                    cachesCount++;
                    cachesURL = cachesEndpointURL + content.ConcatNRemote();
                    cachesPath = Path.Combine(cachesPathType, NormalizePath(content.ConcatN()));

                    if (!Directory.Exists(Path.GetDirectoryName(cachesPath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(cachesPath));

                    cachesFileInfo = new FileInfo(cachesPath);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CachesStatus.Text = $"Downloading {dataType.DataType}: {content.N}";
                    });

                    httpClient.ProgressChanged += CachesDownloadProgress;
                    httpClient.DownloadStream(cachesURL, cachesStream = cachesFileInfo.Create(), cancellationTokenSource.Token);
                    httpClient.ProgressChanged -= CachesDownloadProgress;

                    LogWriteLine($"Downloaded: {content.N}", LogType.Default, true);

                    cachesStream.Dispose();

                    DispatcherQueue.TryEnqueue(() => brokenCachesListUI.RemoveAt(0));
                }
            }
        }

        private void CacheReindexing()
        {
            string cachesPathType;
            string indexPath = Path.Combine(cachesBasePath, "Data", "Verify.txt");

            if (File.Exists(indexPath))
                File.Delete(indexPath);

            using (StreamWriter sw = new StreamWriter(indexPath))
            {
                foreach (DataProperties dataType in cachesList)
                {
                    switch (dataType.DataType)
                    {
                        case CachesType.Data:
                            cachesPathType = Path.Combine(cachesBasePath, "Data");
                            break;
                        default:
                            cachesPathType = Path.Combine(cachesBasePath, "Resources");
                            break;
                    }

                    foreach (DataPropertiesContent content in dataType.Content)
                    {
                        // Why concating "/" between them?
                        // Well, It's just miHoYo's thing.
                        cachesPath = cachesPathType.Replace('\\', '/') + $"//{content.ConcatN()}";
                        sw.WriteLine(cachesPath);
                    }
                }
            }
        }

        Stopwatch refreshTime = Stopwatch.StartNew();
        string timeLeftString;
        private void CachesDownloadProgress(object sender, DownloadProgressChanged e)
        {
            cachesRead += e.CurrentReceived;

            if (refreshTime.Elapsed.Milliseconds > 33)
            {
                refreshTime = Stopwatch.StartNew();
                timeLeftString = string.Format("{0:%h}h{0:%m}m{0:%s}s left", TimeSpan.FromSeconds((cachesTotalSize - cachesRead) / e.CurrentSpeed));
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                CachesTotalStatus.Text = $"Downloading: {cachesCount}/{cachesTotalCount} ({SummarizeSizeSimple(e.CurrentSpeed)}/s) - Estimation: {timeLeftString}";
                CachesTotalProgressBar.Value = GetPercentageNumber(cachesRead, cachesTotalSize);
            });
        }
        private void CachesPartialDownloadProgress(object sender, PartialDownloadProgressChanged e)
        {
            cachesRead += e.CurrentReceived;

            timeLeftString = string.Format("{0:%h}h{0:%m}m{0:%s}s left", TimeSpan.FromSeconds((cachesTotalSize - cachesRead) / e.CurrentSpeed));
            DispatcherQueue.TryEnqueue(() =>
            {
                CachesTotalStatus.Text = $"Downloading: {cachesCount}/{cachesTotalCount} ({SummarizeSizeSimple(e.CurrentSpeed)}/s) - Estimation: {timeLeftString}";
                CachesTotalProgressBar.Value = GetPercentageNumber(cachesRead, cachesTotalSize);
            });
        }
    }
}
