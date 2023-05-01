﻿/*
 * Copyright © 2015 - 2023 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 */

using EliteDangerousCore.EDSM;
using System;
using System.Diagnostics;
using System.Threading;
using EliteDangerousCore;
using EliteDangerousCore.DB;
using System.IO;
using System.Net;

namespace EDDiscovery
{
    public partial class EDDiscoveryController
    {
        private class SystemsSyncState
        {
            public bool perform_fullsync = false;

            public long fullsync_count = 0;
            public long updatesync_count = 0;

            public void ClearCounters()
            {
                fullsync_count = 0;
                updatesync_count = 0;
            }
        }

        private SystemsSyncState syncstate = new SystemsSyncState();

        private int resyncSysDBRequestedFlag = 0;            // flag gets set during SysDB refresh, cleared at end, interlocked exchange during request..

        public bool AsyncPerformSync(bool fullsync)      // UI thread.
        {
            System.Diagnostics.Debug.WriteLine($"Ask for sync start {fullsync}");
            Debug.Assert(System.Windows.Forms.Application.MessageLoop);

            if (Interlocked.CompareExchange(ref resyncSysDBRequestedFlag, 1, 0) == 0)
            {
                syncstate.perform_fullsync |= fullsync;
                resyncRequestedEvent.Set();
                return true;
            }
            else
            {
                return false;
            }
        }

        const int ForceEDSMFullDownloadDays = 56;      // beyond this time, we force a full download
        const int ForceSpanshFullDownloadDays = 170;   // beyond this time, we force a full download
        const int MiniumSpanshUpdateAge = 3;           // beyond this time, we update spansh
        const int EDSMUpdateFetchHours = 12;           // for an update fetch, its these number of hours at a time (Feb 2021 moved to 6 due to EDSM new server)

        public void CheckForSync()      // called in background init
        {
            if (!EDDOptions.Instance.NoSystemsLoad && EDDConfig.Instance.SystemDBDownload)        // if enabled
            {
                DateTime edsmdatetime = SystemsDatabase.Instance.GetLastRecordTimeUTC();

                bool spansh = SystemsDatabase.Instance.GetDBSource().Equals("SPANSH");
                var delta = DateTime.UtcNow.Subtract(edsmdatetime).TotalDays;

                if (delta >= (spansh ? ForceEDSMFullDownloadDays : ForceSpanshFullDownloadDays))
                {
                    System.Diagnostics.Debug.WriteLine("Full system data download ordered, time since {0}", DateTime.UtcNow.Subtract(edsmdatetime).TotalDays);
                    syncstate.perform_fullsync = true;       // do a full sync.
                }

                if (syncstate.perform_fullsync)
                {
                    LogLine(string.Format("System data download from {0} required." + Environment.NewLine +
                                    "This will take a while, please be patient." + Environment.NewLine +
                                    "Please continue running ED Discovery until refresh is complete.".T(EDTx.EDDiscoveryController_SyncEDSM), SystemsDatabase.Instance.GetDBSource()));
                }
            }
            else
            {
                LogLine("Star Data download is disabled. Use Settings panel to reenable".T(EDTx.EDDiscoveryController_SyncOff));
            }
        }

        private void DoPerformSync()        // in Background worker
        {
            System.Diagnostics.Debug.WriteLine($"Do perform sync starts {syncstate.perform_fullsync}");

            InvokeAsyncOnUiThread.Invoke(() => OnSyncStarting?.Invoke());       // tell listeners sync is starting

            resyncSysDBRequestedFlag = 1;     // sync is happening, stop any async requests..

            if (EDDConfig.Instance.SystemDBDownload)      // if system DB is to be loaded
            {
                Debug.WriteLine(BaseUtils.AppTicks.TickCountLap() + " Perform System Data Download");

                try
                {
                    bool[] grids = new bool[GridId.MaxGridID];
                    foreach (int i in GridId.FromString(SystemsDatabase.Instance.GetGridIDs()))
                        grids[i] = true;

                    syncstate.ClearCounters();

                    string sourcetype = SystemsDatabase.Instance.GetDBSource();
                    bool spansh = sourcetype.Equals("SPANSH");

                    if (syncstate.perform_fullsync)
                    {
                        if (syncstate.perform_fullsync && !PendingClose)
                        {
                            // Download new systems
                            try
                            {
                                string downloadfile = Path.Combine(EDDOptions.Instance.AppDataDirectory, "systems.json.gz");

                                ReportSyncProgress("Performing full download of System Data");

                                string url = spansh ? string.Format(EDDConfig.Instance.SpanshSystemsURL, "") : EDDConfig.Instance.EDSMFullSystemsURL;

                                Trace.WriteLine($"{BaseUtils.AppTicks.TickCountLap()} Full system download using URL {url} to {downloadfile}");

#if DEBUGLOAD
                                bool success = true;
                                bool deletefile = false;
                                downloadfile = spansh ? @"c:\code\examples\edsm\systems_1week.json" : @"c:\code\examples\edsm\edsmsystems.1e6.json";
#else
                                bool success = BaseUtils.DownloadFile.HTTPDownloadFile(url, downloadfile, false, out bool newfile);
                                bool deletefile = true;
#endif

                                syncstate.perform_fullsync = false;

                                if (success)
                                {
                                    ReportSyncProgress("Download complete, creating database");

                                    syncstate.fullsync_count = SystemsDatabase.Instance.MakeSystemTableFromFile(downloadfile, grids, () => PendingClose, ReportSyncProgress);

                                    if ( deletefile )
                                        BaseUtils.FileHelpers.DeleteFileNoError(downloadfile);       // remove file - don't hold in storage

                                    if (syncstate.fullsync_count < 0)     // this should always update something, the table is replaced.  If its not, its been cancelled
                                        return;
                                }
                                else
                                {
                                    ReportSyncProgress("");
                                    LogLineHighlight("Failed to download full systems file. Try re-running EDD later");
                                  //  BaseUtils.FileHelpers.DeleteFileNoError(downloadfile);       // remove file - don't hold in storage
                                    return;     // new! if we failed to download, fail here, wait for another time
                                }
                            }
                            catch (Exception ex)
                            {
                                LogLineHighlight("GetAllEDSMSystems exception:" + ex.Message);
                            }
                        }

                    }

                    if (!PendingClose)          // perform an update sync to get any new EDSM data
                    {
                        if (spansh)
                        {
                            DateTime lastrecordtime = SystemsDatabase.Instance.GetLastRecordTimeUTC();
                            var delta = DateTime.UtcNow.Subtract(lastrecordtime).TotalDays;

                            if ( delta >= MiniumSpanshUpdateAge)        // if its older than this, we will do an update
                            {
                                // work out file to grab..

                                string filename = delta < 7 ? "_1week" : delta < 14 ? "_2weeks" : delta < 28 ? "_1month" : "_6months";
                                string url = string.Format(EDDConfig.Instance.SpanshSystemsURL, filename);
                                string downloadfile = Path.Combine(EDDOptions.Instance.AppDataDirectory, "systemsdelta.json.gz");

                                ReportSyncProgress($"Performing partial download of System Data from {url}");

                                bool success = BaseUtils.DownloadFile.HTTPDownloadFile(url, downloadfile, false, out bool newfile);

                                if (success)        // grabbed sucessfully
                                {
                                    ReportSyncProgress("Download complete, updating database");

                                    syncstate.updatesync_count = SystemsDB.ParseJSONFile(downloadfile, grids, ref lastrecordtime, ()=>PendingClose, ReportSyncProgress, "");

                                    System.Diagnostics.Trace.WriteLine($"Downloaded from spansh {syncstate.updatesync_count} to {lastrecordtime}");

                                    SystemsDatabase.Instance.SetLastRecordTimeUTC(lastrecordtime);       // keep on storing this in case next time we get an exception
                                }
                                else
                                {
                                    LogLine("Download of Spansh systems from the server failed (no data returned), will try next time program is run");
                                }

                                BaseUtils.FileHelpers.DeleteFileNoError(downloadfile);       // remove file - don't hold in storage
                            }
                        }
                        else
                        {
                            syncstate.updatesync_count = EDSMUpdateSync(grids, () => PendingClose, ReportSyncProgress);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Swallow Operation Cancelled exceptions
                }
                catch (Exception ex)
                {
                    LogLineHighlight("Check Systems exception: " + ex.Message + Environment.NewLine + "Trace: " + ex.StackTrace);
                }
            }

            InvokeAsyncOnUiThread(() => PerformSyncCompletedonUI());
        }

        // Done in UI thread after DoPerformSync completes

        private void PerformSyncCompletedonUI()
        {
            Debug.Assert(System.Windows.Forms.Application.MessageLoop);

            if (syncstate.fullsync_count > 0 || syncstate.updatesync_count > 0)
                LogLine(string.Format("Systems update complete with {0} systems".T(EDTx.EDDiscoveryController_EDSMU), syncstate.fullsync_count + syncstate.updatesync_count));

            OnSyncComplete?.Invoke(syncstate.fullsync_count, syncstate.updatesync_count);

            ReportSyncProgress("");

            resyncSysDBRequestedFlag = 0;        // releases flag and allow another async to happen

            Debug.WriteLine(BaseUtils.AppTicks.TickCountLap() + " Perform sync completed");
        }

        public long EDSMUpdateSync(bool[] grididallow, Func<bool> PendingClose, Action<string> ReportProgress)
        {
            DateTime lastrecordtime = SystemsDatabase.Instance.GetLastRecordTimeUTC();

            DateTime maximumupdatetimewindow = DateTime.UtcNow.AddDays(-ForceEDSMFullDownloadDays);        // limit download to this amount of days
            if (lastrecordtime < maximumupdatetimewindow)
                lastrecordtime = maximumupdatetimewindow;               // this stops crazy situations where somehow we have a very old date but the full sync did not take care of it

            long updates = 0;

            double fetchmult = 1;

            DateTime minimumfetchspan = DateTime.UtcNow.AddHours(-EDSMUpdateFetchHours / 2);        // we don't bother fetching if last record time is beyond this point

            while (lastrecordtime < minimumfetchspan)                                   // stop at X mins before now, so we don't get in a condition
            {                                                                           // where we do a set, the time moves to just before now, 
                                                                                        // and we then do another set with minimum amount of hours
                if (PendingClose())
                    return updates;

                if ( updates == 0)
                    LogLine("Checking for updated EDSM systems (may take a few moments).");

                EDSMClass edsm = new EDSMClass();

                double hourstofetch = EDSMUpdateFetchHours;        //EDSM new server feb 2021, more capable, 

                DateTime enddate = lastrecordtime + TimeSpan.FromHours(hourstofetch * fetchmult);
                if (enddate > DateTime.UtcNow)
                    enddate = DateTime.UtcNow;

                LogLine($"Downloading systems from UTC {lastrecordtime.ToUniversalTime().ToString()} to {enddate.ToUniversalTime().ToString()}");
                System.Diagnostics.Debug.WriteLine($"Downloading systems from UTC {lastrecordtime.ToUniversalTime().ToString()} to {enddate.ToUniversalTime().ToString()} {hourstofetch}");

                string json = null;
                BaseUtils.ResponseData response;
                try
                {
                    Stopwatch sw = new Stopwatch();
                    response = edsm.RequestSystemsData(lastrecordtime, enddate, timeout: 20000);
                    fetchmult = Math.Max(0.1, Math.Min(Math.Min(fetchmult * 1.1, 1.0), 5000.0 / sw.ElapsedMilliseconds));
                }
                catch (WebException ex)
                {
                    ReportProgress($"EDSM request failed");
                    if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null && ex.Response is HttpWebResponse)
                    {
                        string status = ((HttpWebResponse)ex.Response).StatusDescription;
                        LogLine($"Download of EDSM systems from the server failed ({status}), will try next time program is run");
                    }
                    else
                    {
                        LogLine($"Download of EDSM systems from the server failed ({ex.Status.ToString()}), will try next time program is run");
                    }

                    return updates;
                }
                catch (Exception ex)
                {
                    ReportProgress($"EDSM request failed");
                    LogLine($"Download of EDSM systems from the server failed ({ex.Message}), will try next time program is run");
                    return updates;
                }

                if (response.Error)
                {
                    if ((int)response.StatusCode == 429)
                    {
                        LogLine($"EDSM rate limit hit - waiting 2 minutes");
                        for (int sec = 0; sec < 120; sec++)
                        {
                            if (!PendingClose())
                            {
                                System.Threading.Thread.Sleep(1000);
                            }
                        }
                    }
                    else
                    {
                        LogLine($"Download of EDSM systems from the server failed ({response.StatusCode.ToString()}), will try next time program is run");
                        return updates;
                    }
                }

                json = response.Body;

                if (json == null)
                {
                    ReportProgress("EDSM request failed");
                    LogLine("Download of EDSM systems from the server failed (no data returned), will try next time program is run");
                    return updates;
                }

                // debug File.WriteAllText(@"c:\code\json.txt", json);

                DateTime prevrectime = lastrecordtime;
                System.Diagnostics.Trace.WriteLine($"EDSM partial download last record time {lastrecordtime}");

                long updated = 0;

                try
                {
                    ReportProgress($"EDSM star database update from UTC " + lastrecordtime.ToUniversalTime().ToString() );

                    updated = SystemsDB.ParseJSONString(json, grididallow, ref lastrecordtime, PendingClose, ReportProgress, "");

                    System.Diagnostics.Trace.WriteLine($"EDSM parital download updated {updated} to {lastrecordtime}");

                    // if lastrecordtime did not change (=) or worse still, EDSM somehow moved the time back (unlikely)
                    if (lastrecordtime <= prevrectime)
                    {
                        lastrecordtime += TimeSpan.FromHours(12);       // Lets move on manually so we don't get stuck
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("SysClassEDSM.2 Exception " + e.ToString());
                    ReportProgress("EDSM request failed");
                    LogLine("Processing EDSM systems download failed, will try next time program is run");
                    return updates;
                }

                updates += updated;

                SystemsDatabase.Instance.SetLastRecordTimeUTC(lastrecordtime);       // keep on storing this in case next time we get an exception

                int delay = 10;     // Anthor's normal delay 
                int ratelimitlimit;
                int ratelimitremain;
                int ratelimitreset;

                if (response.Headers != null &&
                    response.Headers["X-Rate-Limit-Limit"] != null &&
                    response.Headers["X-Rate-Limit-Remaining"] != null &&
                    response.Headers["X-Rate-Limit-Reset"] != null &&
                    Int32.TryParse(response.Headers["X-Rate-Limit-Limit"], out ratelimitlimit) &&
                    Int32.TryParse(response.Headers["X-Rate-Limit-Remaining"], out ratelimitremain) &&
                    Int32.TryParse(response.Headers["X-Rate-Limit-Reset"], out ratelimitreset))
                {
                    if (ratelimitremain < ratelimitlimit * 3 / 4)       // lets keep at least X remaining for other purposes later..
                        delay = ratelimitreset / (ratelimitlimit - ratelimitremain);    // slow down to its pace now.. example 878/(360-272) = 10 seconds per quota
                    else
                        delay = 0;

                    System.Diagnostics.Debug.WriteLine("EDSM Delay Parameters {0} {1} {2} => {3}s", ratelimitlimit, ratelimitremain, ratelimitreset, delay);
                }

                for (int sec = 0; sec < delay; sec++)
                {
                    if (!PendingClose())
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }
            }

            return updates;
        }
    }
}

