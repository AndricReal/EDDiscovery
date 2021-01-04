﻿/*
 * Copyright © 2015 - 2016 EDDiscovery development team
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
using EliteDangerousCore;
using EliteDangerousCore.DB;
using System.Collections.Generic;
using System.Windows.Forms;

namespace EDDiscovery._3DMap
{
    public class MapManager
    {
        private FormMap _formMap;

        public MapManager(EDDiscoveryForm frm)
        {
            if (!EDDOptions.Instance.No3DMap)
            {
                _formMap = new FormMap()
                {
                    discoveryForm = frm,
                    TopMost = frm.TopMost
                };
                frm.TopMostChanged += (s, e) => _formMap.TopMost = ((EDDiscoveryForm)s).TopMost;
            }
        }

        public bool Is3DMapsRunning { get { return _formMap.Is3DMapsRunning; } }

        public void Prepare(ISystem historysel, string homesys, ISystem centersys, float zoom, List<HistoryEntry> visited)
        {
            _formMap?.Prepare(historysel, homesys, centersys, zoom, visited);
        }

        public void Prepare(ISystem historysel, ISystem homesys, ISystem centersys, float zoom, List<HistoryEntry> visited)
        {
            _formMap?.Prepare(historysel, homesys, centersys, zoom, visited);
        }

        public void SetPlanned(List<ISystem> plannedr)
        {
            _formMap?.SetPlannedRoute(plannedr);
        }

        public void UpdateHistorySystem(ISystem historysel)
        {
            _formMap?.UpdateHistorySystem(historysel);
        }

        public bool MoveToSystem(ISystem system)
        {
            return _formMap?.SetCenterSystemTo(system) ?? true;
        }

        public bool MoveTo(float x, float y, float z)
        {
            return _formMap?.MoveTo(x, y, z) ?? true;
        }

        public void Show()
        {
            if (_formMap != null)
            {
                if (SystemsDatabase.Instance.RebuildRunning)
                {
                    ExtendedControls.MessageBoxTheme.Show("Not Available due to DB rebuild", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    EDDiscovery.EDDTheme theme = EDDiscovery.EDDTheme.Instance;
                    _formMap.IconSelect(theme.ApplyStd(_formMap));

                    _formMap.Show();
                    _formMap.Focus();
                }
            }
        }
    }
}
