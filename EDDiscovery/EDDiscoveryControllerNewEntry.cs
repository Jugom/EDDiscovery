﻿/*
 * Copyright © 2015 - 2019 EDDiscovery development team
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
 *
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EliteDangerousCore;

namespace EDDiscovery
{
    public partial class EDDiscoveryController
    {
        private Queue<JournalEntry> journalqueue = new Queue<JournalEntry>();
        private System.Threading.Timer journalqueuedelaytimer;

        public void NewEntry(JournalEntry je)        // on UI thread. hooked into journal monitor and receives new entries.. Also call if you programatically add an entry
        {
            Debug.Assert(System.Windows.Forms.Application.MessageLoop);

            int playdelay = HistoryList.MergeTypeDelay(je); // see if there is a delay needed..

            if (playdelay > 0)  // if delaying to see if a companion event occurs. add it to list. Set timer so we pick it up
            {
                System.Diagnostics.Debug.WriteLine(Environment.TickCount + " Delay Play queue " + je.EventTypeID + " Delay for " + playdelay);
                journalqueue.Enqueue(je);
                journalqueuedelaytimer.Change(playdelay, Timeout.Infinite);
            }
            else
            {
                journalqueuedelaytimer.Change(Timeout.Infinite, Timeout.Infinite);  // stop the timer, but if it occurs before this, not the end of the world
                journalqueue.Enqueue(je);  // add it to the play list.
                //System.Diagnostics.Debug.WriteLine(Environment.TickCount + " No delay, issue " + je.EventTypeID );
                PlayJournalList();    // and play
            }
        }

        public void PlayJournalList()                 // UI Threead play delay list out..
        {
            Debug.Assert(System.Windows.Forms.Application.MessageLoop);
            //System.Diagnostics.Debug.WriteLine(Environment.TickCount + " Play out list");

            JournalEntry prev = null;  // we start afresh from the point of merging so we don't merge with previous ones already shown

            while (journalqueue.Count > 0)
            {
                JournalEntry je = journalqueue.Dequeue();

                if (!HistoryList.MergeOrDiscardEntries(prev, je))                // if not merged
                {
                    if (prev != null)                       // no merge, so if we have a merge candidate on top, run actions on it.
                        ActionEntry(prev);

                    prev = je;                              // record
                }
            }

            if (prev != null)                               // any left.. action it
                ActionEntry(prev);
        }

        private void ActionEntry(JournalEntry je)               // UI thread issue the JE to the system
        {
            System.Diagnostics.Trace.WriteLine(string.Format(Environment.NewLine + "New JEntry {0} {1}", je.EventTimeUTC, je.EventTypeStr));

            OnNewJournalEntry?.Invoke(je);          // Always call this on all entries...

            // filter out commanders, and filter out any UI events
            if (je.CommanderId == history.CommanderId)
            {
                BaseUtils.AppTicks.TickCountLapDelta("CTNE", true);

                var historyentries = history.AddJournalEntryToHistory(je, h => LogLineHighlight(h));        // add a new one on top, return a list of ones to process

                var t1 = BaseUtils.AppTicks.TickCountLapDelta("CTNE");
                if (t1.Item2 >= 20)
                    System.Diagnostics.Trace.WriteLine(" NE Add Journal slow " + t1.Item1);

                foreach( var he in historyentries.EmptyIfNull())
                {
                    if ( OnNewEntry != null)
                    {
                        foreach (var e in OnNewEntry.GetInvocationList())       // do the invokation manually, so we can time each method
                        {
                            Stopwatch sw = new Stopwatch(); sw.Start();
                            e.DynamicInvoke(he, history);
                            if ( sw.ElapsedMilliseconds >= 20)
                                System.Diagnostics.Trace.WriteLine(" NE Add Method " + e.Method.DeclaringType + " took " + sw.ElapsedMilliseconds);
                        }
                    }

                    var t2 = BaseUtils.AppTicks.TickCountLapDelta("CTNE");
                    if (t2.Item2 >= 40)
                        System.Diagnostics.Trace.WriteLine(" NE First Slow " + t2.Item1);

                    OnNewEntrySecond?.Invoke(he, history);      // secondary hook..

                    // finally, CAPI, if docked, and CAPI is go for pc commander, do capi procedure

                    if (he.EntryType == JournalTypeEnum.Docked && FrontierCAPI.Active && !EDCommander.Current.ConsoleCommander)
                    {
                        var dockevt = he.journalEntry as EliteDangerousCore.JournalEvents.JournalDocked;
                        DoCAPI(dockevt.StationName, he.System.Name, he.journalEntry.IsBeta, history.Shipyards.AllowCobraMkIV);
                    }

                    var t3 = BaseUtils.AppTicks.TickCountLapDelta("CTNE");
                    System.Diagnostics.Trace.WriteLine("NE END " + t3.Item1 + " " + (t3.Item3 > 99 ? "!!!!!!!!!!!!!" : ""));
                }
            }

            if (je.EventTypeID == JournalTypeEnum.LoadGame) // and issue this on Load game
            {
                OnRefreshCommanders?.Invoke();
            }
        }

        public void DelayPlay(Object s)             // timer thread timeout after play delay.. 
        {
            System.Diagnostics.Debug.WriteLine(Environment.TickCount + " Delay Play timer executed");
            journalqueuedelaytimer.Change(Timeout.Infinite, Timeout.Infinite);
            InvokeAsyncOnUiThread(() =>
            {
                PlayJournalList();
            });
        }

        void NewUIEvent(UIEvent u)                  // UI thread new event
        {
            Debug.Assert(System.Windows.Forms.Application.MessageLoop);
            //System.Diagnostics.Debug.WriteLine("Dispatch from controller UI event " + u.EventTypeStr);

            BaseUtils.AppTicks.TickCountLapDelta("CTUI", true);

            var uifuel = u as EliteDangerousCore.UIEvents.UIFuel;       // UI Fuel has information on fuel level - update it.
            if (uifuel != null && history != null)
            {
                history.ShipInformationList.UIFuel(uifuel);             // update the SI global value
                history.GetLast?.UpdateShipInformation(history.ShipInformationList.CurrentShip);    // and make the last entry have this updated info.
            }

            OnNewUIEvent?.Invoke(u);

            var t = BaseUtils.AppTicks.TickCountLapDelta("CTUI");
            if ( t.Item2 > 25 )
                System.Diagnostics.Debug.WriteLine( t.Item1 + " Controller UI !!!");
        }

        public void DoCAPI(string station, string system, bool beta , bool? allowcobramkiv)
        {
            // don't hold up the main thread, do it in a task, as its a HTTP operation

            System.Threading.Tasks.Task.Run(() =>
            {
                bool donemarket = false, doneshipyard = false;

                for (int tries = 3; tries >= 1 && (donemarket == false || doneshipyard == false); tries--)
                {
                    Thread.Sleep(10000);        // for the first go, give the CAPI servers a chance to update, for the next goes, spread out the requests

                    FrontierCAPI.GameIsBeta = beta;

                    if (!donemarket)
                    {
                        string marketjson = FrontierCAPI.Market();

                        if ( marketjson != null )
                        {
                            System.IO.File.WriteAllText(@"c:\code\market.json", marketjson);

                            CAPI.Market mk = new CAPI.Market(marketjson);
                            if (mk.IsValid && station.Equals(mk.Name, StringComparison.InvariantCultureIgnoreCase))
                            {
                                System.Diagnostics.Trace.WriteLine($"CAPI got market {mk.Name}");

                                var entry = new EliteDangerousCore.JournalEvents.JournalEDDCommodityPrices(DateTime.UtcNow,
                                                mk.ID, mk.Name, system, EDCommander.CurrentCmdrID, mk.Commodities);

                                var jo = entry.ToJSON();        // get json of it, and add it to the db
                                entry.Add(jo);

                                InvokeAsyncOnUiThread(() =>
                                {
                                    Debug.Assert(System.Windows.Forms.Application.MessageLoop);
                                    NewEntry(entry);                // then push it thru. this will cause another set of calls to NewEntry First/Second
                                                                    // EDDN handler will pick up EDDCommodityPrices and send it.
                                });

                                donemarket = true;
                                Thread.Sleep(500);      // space the next check out a bit
                            }
                        }
                    }

                    if (!donemarket)
                    {
                        LogLine("CAPI failed to get market data" + (tries > 1 ? ", retrying" : ", give up"));
                    }

                    if (!doneshipyard)
                    {
                        string shipyardjson = FrontierCAPI.Shipyard();

                        if (shipyardjson != null)
                        {
                            CAPI.Shipyard sh = new CAPI.Shipyard(shipyardjson);
                            System.IO.File.WriteAllText(@"c:\code\shipyard.json", shipyardjson);
                            if (sh.IsValid && station.Equals(sh.Name, StringComparison.InvariantCultureIgnoreCase))
                            {
                                System.Diagnostics.Trace.WriteLine($"CAPI got shipyard {sh.Name}");

                                var modules = sh.GetModules();
                                if ( modules?.Count > 0 )
                                {
                                    var list = modules.Select(x => new Tuple<long, string, long>(x.ID, x.Name.ToLower(), x.Cost)).ToArray();
                                    var outfitting = new EliteDangerousCore.JournalEvents.JournalOutfitting(DateTime.UtcNow, station, system, sh.ID, list, EDCommander.CurrentCmdrID);

                                    var jo = outfitting.ToJSON();        // get json of it, and add it to the db
                                    outfitting.Add(jo);

                                    InvokeAsyncOnUiThread(() =>
                                    {
                                        NewEntry(outfitting);                // then push it thru. this will cause another set of calls to NewEntry First/Second, then EDDN will send it
                                    });
                                }

                                var shipyard = sh.GetShips();

                                if ( shipyard?.Count > 0 && allowcobramkiv.HasValue)
                                {
                                    var list = shipyard.Select(x => new Tuple<long, string, long>(x.ID, x.Name.ToLower(), x.BaseValue)).ToArray();
                                    var shipyardevent = new EliteDangerousCore.JournalEvents.JournalShipyard(DateTime.UtcNow, station, system, sh.ID, list, EDCommander.CurrentCmdrID, allowcobramkiv.Value);

                                    var jo = shipyardevent.ToJSON();        // get json of it, and add it to the db
                                    shipyardevent.Add(jo);

                                    InvokeAsyncOnUiThread(() =>
                                    {
                                        NewEntry(shipyardevent);                // then push it thru. this will cause another set of calls to NewEntry First/Second, then EDDN will send it
                                    });
                                }

                                doneshipyard = true;
                            }
                        }
                    }

                    if (!doneshipyard)
                    {
                        LogLine("CAPI failed to get shipyard data" + (tries > 1 ? ", retrying" : ", give up"));
                    }
                }

            });

        }


    }
}
