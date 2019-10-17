﻿using Microsoft.Xna.Framework;
using System;

namespace OpenVIII
{
    public class IGM_LGSG : Menu
    {
        [Flags]
        public enum Mode : byte
        {
            /// <summary>
            /// Loading or nothing
            /// </summary>
            Nothing = 0x0,
            /// <summary>
            /// Save flag to switch to saving instead of loading.
            /// </summary>
            Save = 0x1,
            /// <summary>
            /// MemoryCard slots or Gamefolders
            /// </summary>
            Slot = 0x2,
            /// <summary>
            /// Game list.
            /// </summary>
            Game = 0x4,
            /// <summary>
            /// choose from list.
            /// </summary>
            Choose = 0x8,
            /// <summary>
            /// Load bar
            /// </summary>
            Checking = 0x10,
            /// <summary>
            /// Header
            /// </summary>
            Header = 0x20,
        }

        protected override void Init()
        {
            Data[Mode.Slot | Mode.Choose] = null;
            Data[Mode.Slot | Mode.Checking] = null;
            Data[Mode.Game | Mode.Choose] = null;
            Data[Mode.Game | Mode.Checking] = null;
            Data[Mode.Header] = null;
            Data[Mode.Save | Mode.Slot | Mode.Choose] = null;
            Data[Mode.Save | Mode.Slot | Mode.Checking] = null;
            Data[Mode.Save | Mode.Game | Mode.Choose] = null;
            Data[Mode.Save | Mode.Game | Mode.Checking] = null;
            Data[Mode.Save | Mode.Header] = null;
            base.Init();
        }
    }
    namespace IGMData
    {
        public class ThreePieceHeader : IGMData.Base
        {
            static public ThreePieceHeader Create(FF8String topleft, FF8String topright, FF8String help, Rectangle pos)
            {
                var r = Create<ThreePieceHeader>(3, 1, new IGMDataItem.Empty(pos));
                r.TOPLeft.Data = topleft;
                r.TOPRight.Data = topright;
                r.HELP.Data = help;
                return r;
            }
            protected override void Init()
            {
                base.Init();
                int space = 4;
                ITEM[0, 0] = new IGMDataItem.Box{Pos=new Rectangle(0, 0, (int)(Width * 0.82f), (Height - space) / 2)};
                ITEM[1, 0] = new IGMDataItem.Box{Pos=new Rectangle(ITEM[0, 0].Width + space, 0, (int)(Width * 0.18f - space), ITEM[0, 0].Height)};
                ITEM[2, 0] = new IGMDataItem.Box{Pos=new Rectangle((int)(Width * 4f), ITEM[0, 0].Height + space, (int)(Width * 0.94f), ITEM[0, 0].Height),Title=Icons.ID.HELP};
            }
            protected IGMDataItem.Box TOPLeft => (IGMDataItem.Box)ITEM[0, 0];
            protected IGMDataItem.Box TOPRight => (IGMDataItem.Box)ITEM[1, 0];
            protected IGMDataItem.Box HELP => (IGMDataItem.Box)ITEM[2, 0];

            public void Refresh(FF8String topleft, FF8String topright, FF8String help)
            {
                TOPLeft.Data = topleft;
                TOPRight.Data = topright;
                HELP.Data = help;
                Refresh();
            }

        }
        public class LoadBarBox : IGMData.Base
        {
            public LoadBarBox Create(Rectangle pos) => Create<LoadBarBox>(2, 1, container: new IGMDataItem.Box{Pos= pos,Title= Icons.ID.INFO});
            protected override void Init()
            {
                base.Init();
                int height = 8;
                int y = Height / 2 - height / 2;
                ITEM[0, 0] = new IGMDataItem.Icon(Icons.ID.Bar_BG, new Rectangle((int)(0.05f * Width), y,(int)(Width *.9f),height
                    ));
                ITEM[1, 0] = new IGMDataItem.Icon(Icons.ID.Bar_Fill,ITEM[0,0].Pos);
            }
        }
    }
}