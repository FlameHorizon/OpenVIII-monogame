﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace OpenVIII
{
    public abstract class Menu
    {

        #region Fields

        public static EventHandler FadedInHandler;
        public static EventHandler FadedOutHandler;
        public Dictionary<Enum, IGMData> Data;

        public EventHandler<Enum> ModeChangeHandler;
        protected Enum _mode;
        protected bool skipdata;

        /// <summary>
        /// <para>Time to fade out in milliseconds</para>
        /// <para>Larger is slower</para>
        /// </summary>
        private const int _blinkinspeed = 500;

        /// <summary>
        /// <para>Time to fade out in milliseconds</para>
        /// <para>Larger is slower</para>
        /// </summary>
        private const int _blinkoutspeed = 900;

        private const float _fadedin = 1f;
        private const float _fadedout = 0f;

        /// <summary>
        /// <para>Time to fade out in milliseconds</para>
        /// <para>Larger is slower</para>
        /// </summary>
        private const int _fadeinspeed = 700;

        /// <summary>
        /// <para>Time to fade out in milliseconds</para>
        /// <para>Larger is slower</para>
        /// </summary>
        private const int _fadeoutspeed = 1500;

        private static bool _blinkstate;
        private static bool _fadeout = false;
        private static IGM _igm;

        private static IGM_Items _igm_items;

        private static IGM_Junction _igm_junction;

        private static IGM_Lobby _igm_lobby;

        private static object _igm_lock = new object();
        private Vector2 _size;
        private static BattleMenus _battlemenus;

        #endregion Fields

        #region Constructors

        public Menu()
        {
            InitConstructor();
        }

        public Menu(Characters character, Characters? visablecharacter = null)
        {
            Character = character;
            VisableCharacter = visablecharacter ?? character;
            InitConstructor(); // because base() would always run first :(
        }
        private void InitConstructor()
        {
            //WaitForInit();
            if (!cancel)
            {
                Data = new Dictionary<Enum, IGMData>();
                Init();
                skipdata = true;
                ReInit();
                skipdata = false;
            }
        }
        #endregion Constructors

        #region Properties

        public static float Blink_Amount { get; private set; } = _fadedin;

        public static float Fade { get; private set; } = _fadedin;

        public static Matrix Focus { get; protected set; }

        public static IGM IGM => _igm;

        public static IGM_Items IGM_Items => _igm_items;

        public static IGM_Junction IGM_Junction => _igm_junction;

        public static IGM_Lobby IGM_Lobby => _igm_lobby;

        public static BattleMenus BattleMenus => _battlemenus;

        public static Vector2 TextScale { get; } = new Vector2(2.545455f, 3.0375f);

        public bool Enabled { get; protected set; } = true;

        public Vector2 Size { get => _size; protected set => _size = value; }

        public static Point MouseLocation => Input.MouseLocation.Transform(Menu.Focus);

        /// <summary>
        /// Character who has the junctions and inventory. Same as VisableCharacter unless TeamLaguna.
        /// </summary>
        protected Characters Character { get; set; }

        /// <summary>
        /// Required to support Laguna's Party. They have unique stats but share junctions and inventory.
        /// </summary>
        protected Characters VisableCharacter { get; set; }

        protected Vector2 vp => new Vector2(Memory.graphics.GraphicsDevice.Viewport.Width, Memory.graphics.GraphicsDevice.Viewport.Height);

        private bool cancel => Memory.Token.IsCancellationRequested;

        #endregion Properties

        #region Methods

        public static Tuple<Rectangle, Point, Rectangle> DrawBox(Rectangle dst, FF8String buffer = null, Icons.ID? title = null, Vector2? textScale = null, Vector2? boxScale = null, Box_Options options = Box_Options.Default)
        {
            if (textScale == null) textScale = Vector2.One;
            if (boxScale == null) boxScale = Vector2.One;
            Point cursor = Point.Zero;
            dst.Size = (dst.Size.ToVector2()).ToPoint();
            dst.Location = (dst.Location.ToVector2()).ToPoint();
            Vector2 bgscale = new Vector2(2f) * textScale.Value;
            Rectangle box = dst.Scale(boxScale.Value);
            Rectangle backup = dst;
            Rectangle hotspot = new Rectangle(dst.Location, dst.Size);
            Rectangle font = new Rectangle();
            if ((options & Box_Options.SkipDraw) == 0)
            {
                if (dst.Width > 256 * bgscale.X)
                    Memory.Icons.Draw(Icons.ID.Menu_BG_368, 0, box, bgscale, Fade);
                else
                    Memory.Icons.Draw(Icons.ID.Menu_BG_256, 0, box, bgscale, Fade);
                if (title != null)
                {
                    //dst.Size = (Memory.Icons[title.Value].GetRectangle.Size.ToVector2()  * 2.823317308f).ToPoint();
                    dst.Offset(15, 0);
                    dst.Y = (int)(dst.Y * boxScale.Value.Y);
                    Memory.Icons.Draw(title.Value, 2, dst, (bgscale + new Vector2(.5f)), Fade);
                }
                dst = backup;
            }
            if (buffer != null && buffer.Length > 0)
            {
                font = Memory.font.RenderBasicText(buffer, dst.Location.ToVector2(), TextScale * textScale.Value, Fade: Fade, skipdraw: true);
                if ((options & Box_Options.Indent) != 0)
                    dst.Offset(70 * textScale.Value.X, 0);
                else if ((options & Box_Options.Center) != 0)
                    dst.Offset(dst.Width / 2 - font.Width / 2, 0);
                else
                    dst.Offset(25 * textScale.Value.X, 0);

                if ((options & Box_Options.Buttom) != 0)
                    dst.Offset(0, (dst.Height - 48));
                else if ((options & Box_Options.Middle) != 0)
                    dst.Offset(0, dst.Height / 2 - font.Height / 2);
                else
                    dst.Offset(0, 21);

                dst.Y = (int)(dst.Y * boxScale.Value.Y);
                font = Memory.font.RenderBasicText(buffer, dst.Location.ToVector2(), TextScale * textScale.Value, Fade: Fade, skipdraw: (options & Box_Options.SkipDraw) != 0);
                cursor = dst.Location;
                cursor.Y += (int)(TextScale.Y * 6); // 12 * (3.0375/2)
            }
            return new Tuple<Rectangle, Point, Rectangle>(hotspot, cursor, font);
        }

        public static void DrawPointer(Point cursor, Vector2? offset = null, bool blink = false)
        {
            if (offset == null)
                offset = new Vector2(-1.15f, -.3f);
            Vector2 scale = new Vector2(2f);
            Vector2 size = Memory.Icons.GetEntry(Icons.ID.Finger_Right, 0).Size * scale;
            Rectangle dst = new Rectangle(cursor, Point.Zero);
            byte pallet = 2;
            byte fadedpallet = 7;
            dst.Offset(size * offset.Value);
            if (blink)
            {
                Memory.Icons.Draw(Icons.ID.Finger_Right, fadedpallet, dst, scale, Fade);
            }
            Memory.Icons.Draw(Icons.ID.Finger_Right, pallet, dst, scale, blink ? Fade * Blink_Amount : Fade);
        }

        public static void FadeIn()
        {
            Fade = _fadedout;
            _fadeout = false;
        }

        public static void FadeOut()
        {
            Fade = _fadedin;
            _fadeout = true;
        }

        public static void InitStaticMembers()
        {
            lock (_igm_lock)
            {
                if (_igm_lobby == null)
                    _igm_lobby = new IGM_Lobby();
                if (_igm == null)
                    _igm = new IGM();
                if (_igm_junction == null)
                    _igm_junction = new IGM_Junction();
                if (_igm_items == null)
                    _igm_items = new IGM_Items();
                if (_battlemenus ==null)
                    _battlemenus = new BattleMenus();
                Fade = 0;
            }
        }

        public static void UpdateFade(object sender = null)
        {

            if (_blinkstate)
            {
                Blink_Amount += (float)(Memory.gameTime.ElapsedGameTime.TotalMilliseconds / _blinkinspeed);
                if (Blink_Amount >= _fadedin) _blinkstate = false;
            }
            else
            {
                Blink_Amount -= (float)(Memory.gameTime.ElapsedGameTime.TotalMilliseconds / _blinkoutspeed);
                if (Blink_Amount <= _fadedout) _blinkstate = true;
            }
            if (!_fadeout && Fade < _fadedin)
            {
                Fade += (float)(Memory.gameTime.ElapsedGameTime.TotalMilliseconds / _fadeinspeed);
                if (Fade >= _fadedin)
                {
                    FadedInHandler?.Invoke(sender, null);
                }
            }
            else if (_fadeout && Fade > _fadedout)
            {
                Fade -= (float)(Memory.gameTime.ElapsedGameTime.TotalMilliseconds / _fadeoutspeed);
                if (Fade <= _fadedout)
                {
                    _fadeout = false;
                    FadedOutHandler?.Invoke(sender, null);
                }
            }
        }

        public virtual void Draw()
        {
            StartDraw();
            DrawData();
            EndDraw();
        }

        public virtual void DrawData()
        {
            if (!skipdata && Enabled)
                foreach (KeyValuePair<Enum, IGMData> i in Data)
                    i.Value.Draw();
        }

        public virtual void EndDraw()
        {
            if (Enabled)
                Memory.SpriteBatchEnd();
        }

        public Enum GetMode() => _mode;

        public virtual void Hide() => Enabled = false;

        public virtual void ReInit()
        {
            if (!skipdata)
                foreach (KeyValuePair<Enum, IGMData> i in Data)
                    i.Value.ReInit(Character, VisableCharacter);
        }

        public virtual void ReInit(Characters c, Characters vc, bool backup = false)
        {
            Character = c;
            VisableCharacter = vc;
            //backup memory

            if (backup)
                Memory.PrevState = Memory.State.Clone();
            ReInit();
        }

        public void SetMode(Enum mode)
        {
            if (!mode.Equals(_mode))
            {
                ModeChangeHandler?.Invoke(this, mode);
                _mode = mode;
            }
        }

        public virtual void Show() => Enabled = true;

        public virtual void StartDraw()
        {
            if (Enabled)
                Memory.SpriteBatchStartAlpha(ss: SamplerState.PointClamp, tm: Focus);
        }
        public virtual bool Update()
        {
            bool ret = false;
            UpdateFade(this);
            GenerateFocus();
            if (Enabled)
            {
                //todo detect when there is no saves detected.
                //check for null
                if (!skipdata)
                    foreach (KeyValuePair<Enum, IGMData> i in Data)
                    {
                        ret = i.Value.Update() || ret;
                    }
            }
            return Inputs() || ret;
        }
        
        protected void GenerateFocus(Vector2? inputsize = null, Box_Options options = Box_Options.Default)
        {
            Vector2 size = inputsize ?? Size;
            Vector2 Zoom = Memory.Scale(size.X, size.Y, Memory.ScaleMode.FitBoth);
            size /= 2;
            Vector2 t = new Vector2(vp.X / 2, vp.Y / 2);
            if ((options & Box_Options.Top) != 0)
            {
                t.Y = 0;
                size.Y = 0;
            }
            else if ((options & Box_Options.Buttom)!=0)
            {
                t.Y = vp.Y - (size.Y * 2 * Zoom.Y);
                size.Y = 0;
            }
            Focus = Matrix.CreateTranslation(-size.X , -size.Y, 0) *
                Matrix.CreateScale(new Vector3(Zoom.X, Zoom.Y, 1)) *
                Matrix.CreateTranslation(t.X, t.Y, 0);
        }

        protected virtual void Init()
        {
        }

        protected abstract bool Inputs();

        #endregion Methods

        //private async void WaitForInit()
        //{
        //    while (!Memory.Inited)
        //    {
        //        await Memory.InitTask;
        //    }
        //}
    }
}