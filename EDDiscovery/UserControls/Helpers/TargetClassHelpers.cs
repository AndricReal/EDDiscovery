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

using EliteDangerousCore.EDSM;
using EliteDangerousCore;
using EliteDangerousCore.DB;
using System;

using System.Windows.Forms;

namespace EDDiscovery.UserControls
{
    static class TargetHelpers
    {
        // Set or clear a target.  targetname = empty/null means delete
        // a target needs a system lookup to be successful, or a GMO object
        // targets are associated with bookmarks or a note on the system (any note)
        // if no note/bookmark is found, a bookmark is prompted to be made

        public static void SetTargetSystem(Object sender, EDDiscoveryForm discoveryform, string targetname, bool prompt = false)
        {
            Form senderForm = ((Control)sender)?.FindForm() ?? discoveryform;

            if (string.IsNullOrWhiteSpace(targetname))      // if empty, delete it
            {
                if (prompt && TargetClass.IsTargetSet())      // if prompting, and target is set, ask for delete
                {
                    if (ExtendedControls.MessageBoxTheme.Show(senderForm, "Confirm deletion of target", "Delete a target", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                    {
                        TargetClass.ClearTarget();
                        discoveryform.NewTargetSet(sender);          // tells everyone who cares a new target was set
                    }
                }

                return;
            }

            // find system 

            ISystem sc = SystemCache.FindSystem(targetname, discoveryform.galacticMapping, true);
            string msgboxtext = null;

            if (sc != null && sc.HasCoordinate)         // if we have a system, and it has co-ords
            {
                SystemNoteClass nc = SystemNoteClass.GetLastNoteOnSystem(sc.Name);        // has it got a note?

                if (nc != null)     // had a note, lets associate with a note
                {
                    TargetClass.SetTargetNotedSystem(sc.Name, nc.id, sc.X, sc.Y, sc.Z);
                    msgboxtext = "Target set on system with note " + sc.Name;
                }
                else
                {
                    BookmarkClass bk = GlobalBookMarkList.Instance.FindBookmarkOnSystem(targetname);    // has it been bookmarked?

                    if (bk != null)     // yep, associate with a bookmark
                    {
                        TargetClass.SetTargetBookmark(sc.Name, bk.id, bk.x, bk.y, bk.z);
                        msgboxtext = "Target set on bookmarked system " + sc.Name;
                    }
                    else
                    {
                        // create bookmark for it

                        bool createbookmark = false;
                        if ((prompt && ExtendedControls.MessageBoxTheme.Show(senderForm, "Make a bookmark on " + sc.Name + " and set as target?", "Make Bookmark", MessageBoxButtons.OKCancel) == DialogResult.OK) || !prompt)
                        {
                            createbookmark = true;
                        }

                        if (createbookmark)
                        {
                            BookmarkClass newbk = GlobalBookMarkList.Instance.AddOrUpdateBookmark(null, true, targetname, sc.X, sc.Y, sc.Z, DateTime.UtcNow, "");
                            TargetClass.SetTargetBookmark(sc.Name, newbk.id, newbk.x, newbk.y, newbk.z);
                        }
                    }
                }

            }
            else
            {
                // system not known to star database. See if its a GMO thingy

                if (targetname.Length > 2 && targetname.Substring(0, 2).Equals("G:"))
                    targetname = targetname.Substring(2, targetname.Length - 2);

                GalacticMapObject gmo = discoveryform.galacticMapping.Find(targetname, true);    // ignore if its off, find any part of string, find if disabled

                if (gmo != null)        // yes, so grab the address
                {
                    TargetClass.SetTargetGMO("G:" + gmo.Name, gmo.ID, gmo.Points[0].X, gmo.Points[0].Y, gmo.Points[0].Z);
                    msgboxtext = "Target set on galaxy object " + gmo.Name;
                }
                else
                {
                    msgboxtext = "Unknown system, system is without co-ordinates or galaxy object not found";
                }
            }

            discoveryform.NewTargetSet(sender);          // tells everyone who cares a new target was set

            if (msgboxtext != null && prompt)
                ExtendedControls.MessageBoxTheme.Show(senderForm, msgboxtext, "Create a target", MessageBoxButtons.OK);

        }
    }
}
