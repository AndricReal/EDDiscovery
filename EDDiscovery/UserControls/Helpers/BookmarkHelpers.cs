/*
 * Copyright © 2016-2022 EDDiscovery development team
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

using EDDiscovery.Forms;
using EliteDangerousCore;
using EliteDangerousCore.DB;
using System;

using System.Windows.Forms;

namespace EDDiscovery.UserControls
{
    static class BookmarkHelpers
    { 
        // cursystem = null, curbookmark = null, new system free entry bookmark
        // cursystem != null, curbookmark = null, system bookmark found, update
        // cursystem != null, curbookmark = null, no system bookmark found, new bookmark on system
        // curbookmark != null, edit current bookmark

        public static void ShowBookmarkForm(Object sender, EDDiscoveryForm discoveryForm, ISystem cursystem, BookmarkClass curbookmark, bool notedsystem)
        {
            Form senderForm = ((Control)sender)?.FindForm() ?? discoveryForm;

            // try and find the associated bookmark..
            BookmarkClass bkmark = (curbookmark != null) ? curbookmark : (cursystem != null ? GlobalBookMarkList.Instance.FindBookmarkOnSystem(cursystem.Name) : null);

            SystemNoteClass sn = (cursystem != null) ? SystemNoteClass.GetLastNoteOnSystem(cursystem.Name) : null;
            string note = (sn != null) ? sn.Note : "";

            BookmarkForm frm = new BookmarkForm(discoveryForm.history);

            if (notedsystem && bkmark == null)              // note on a system
            {
                long targetid = TargetClass.GetTargetNotedSystem();      // who is the target of a noted system (0=none)
                long noteid = sn.id;

                frm.InitialisePos(cursystem);
                frm.NotedSystem(cursystem.Name, note, noteid == targetid);       // note may be passed in null
                frm.ShowDialog(senderForm);

                if ((frm.IsTarget && targetid != noteid) || (!frm.IsTarget && targetid == noteid)) // changed..
                {
                    if (frm.IsTarget)
                        TargetClass.SetTargetNotedSystem(cursystem.Name, noteid, cursystem.X, cursystem.Y, cursystem.Z);
                    else
                        TargetClass.ClearTarget();
                }
            }
            else
            {
                bool regionmarker = false;
                DateTime timeutc;

                if (bkmark == null)                         // new bookmark
                {
                    timeutc = DateTime.UtcNow;
                    if (cursystem == null)
                        frm.NewFreeEntrySystemBookmark(timeutc);
                    else
                        frm.NewSystemBookmark(cursystem, note, timeutc);
                }
                else                                        // update bookmark
                {
                    regionmarker = bkmark.isRegion;
                    timeutc = bkmark.TimeUTC;
                    frm.Bookmark(bkmark);
                }

                DialogResult res = frm.ShowDialog(senderForm);

                long curtargetid = TargetClass.GetTargetBookmark();      // who is the target of a bookmark (0=none)

                if (res == DialogResult.OK)
                {
                    BookmarkClass newcls = GlobalBookMarkList.Instance.AddOrUpdateBookmark(bkmark, !regionmarker, frm.StarHeading, double.Parse(frm.x), double.Parse(frm.y), double.Parse(frm.z),
                                                                     timeutc, frm.Notes, frm.SurfaceLocations);


                    if ((frm.IsTarget && curtargetid != newcls.id) || (!frm.IsTarget && curtargetid == newcls.id)) // changed..
                    {
                        if (frm.IsTarget)
                            TargetClass.SetTargetBookmark(regionmarker ? ("RM:" + newcls.Heading) : newcls.StarName, newcls.id, newcls.x, newcls.y, newcls.z);
                        else
                            TargetClass.ClearTarget();
                    }
                }
                else if (res == DialogResult.Abort && bkmark != null)
                {
                    if (curtargetid == bkmark.id)
                    {
                        TargetClass.ClearTarget();
                    }

                    GlobalBookMarkList.Instance.Delete(bkmark);
                }
            }

            discoveryForm.NewTargetSet(sender);
        }
    }
}
