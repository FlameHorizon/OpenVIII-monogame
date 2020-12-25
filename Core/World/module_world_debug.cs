﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenVIII.World;
using OpenVIII.Encoding.Tags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenVIII
{
    public class Module_world_debug
    {
        private static bool bUseCustomShaderTest = true; //enable for testing the shader- mostly learning stuff

        private static FPS_Camera fps_camera;
        public static Matrix projectionMatrix, viewMatrix, worldMatrix;
        public static float degrees;
        private static float camDistance = 120.0f;
        private static float camHeight = 160.0f;
        private static float cameraFOV = 60;
        public static float renderCamDistance = 1200f;
        public static Vector3 camPosition, camTarget;
        public static Vector3 playerPosition = new Vector3(-9105f, 30f, -4466);
        private static Vector3 lastPlayerPosition = playerPosition;
        public static BasicEffect effect;
        public static AlphaTestEffect ate;
        public static Effect worldShaderModel;
        private const float BEND_VALUE = 1.4f;
        private const float BEND_DISTANCE = 350.0f;
        private static readonly Vector3 BEND_VECTOR = new Vector3(0, -0.01f, 0);
        private static readonly Vector4 FOG_COLOR = new Vector4(2f, 2f, 2f, 0f);
        private static Vector3 skyColor = Vector3.One;
        public static float debugVar = 1f;

        private enum _worldState
        {
            _0init,
            _1active,
            _9debugFly
        }

        private enum MiniMapState
        {
            noMinimap,
            planet,
            rectangle,
            fullscreen
        }

        //DEBUG
        private const float WORLD_SCALE_MODEL = 16f;

        private static readonly float FOV = 60;

        public static Vector2 segmentPosition;
        private static CharaOne chara;
        private static texl texl;
        public static Wmset wmset;
        private static wm2field wm2field;
        private static rail rail;

        private static byte[] wmx;
        private static int debugEncounter = -1;
        private const int WM_SEG_SIZE = 0x9000; //World map segment size in file
        private const int WM_SEGMENTS_COUNT = 835;

        #region structures

        private static Segment[] segments;

        private struct Segment
        {
            public int segmentId;
            public SegHeader headerData;
            public Block[] block;

            /// <summary>
            /// parsedTriangle is a struct containing pre-calculated values for world map so the
            /// calculations are one-time operation
            /// </summary>
            public ParsedTriangleData[] parsedTriangle;
        }

        public struct ParsedTriangleData
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public Vector2 uvA;
            public Vector2 uvB;
            public Vector2 uvC;
            public Polygon parentPolygon;
            public BoundingBox boundingBox;
        }

        private struct Block
        {
            public byte polyCount;
            public byte vertCount;
            public byte normalCount;
            public byte unkPadd;
            public Polygon[] polygons;
            public Vertex[] vertices;
            public Normal[] normals;
            public int unkPadd2;
        }

        [StructLayout(LayoutKind.Sequential, Size = 68, Pack = 1)]
        private struct SegHeader
        {
            public uint groupId;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public uint[] blockOffsets;
        }

#pragma warning disable 0649 //Yes, we know- it's expected here

        public struct Polygon
        {
            public byte F1, F2, F3, N1, N2, N3, U1, V1, U2, V2, U3, V3, TPage_clut, groundtype;
            //private byte texSwitch, flags;

            public Texflags texFlags;
            public byte vertFlags;

            public byte TPage => (byte)((TPage_clut >> 4) & 0x0F);
            public byte Clut => (byte)(TPage_clut & 0x0F);
            private Texflags TexFlags { get => Texflags.TEXFLAGS_ISENTERABLE | Texflags.TEXFLAGS_MISC | Texflags.TEXFLAGS_ROAD | Texflags.TEXFLAGS_SHADOW | Texflags.TEXFLAGS_UNK | Texflags.TEXFLAGS_TRANSPARENT | Texflags.TEXFLAGS_WATER; set => texFlags = value; }
            //public byte TPage_clut1 { set => TPage_clut = value; }

            public override string ToString() => $"GP={groundtype.ToString()} TP={TPage.ToString()} Clut={Clut.ToString()} TexFlags={Convert.ToString((byte)texFlags, 2).PadLeft(8, '0')} vertFlags={Convert.ToString(vertFlags, 2).PadLeft(8, '0')} UV={U1.ToString()} {V1.ToString()} {U2.ToString()} {V2.ToString()} {U3.ToString()} {V3.ToString()}";
        }

        private struct Vertex
        {
            public short X;
            private short Z;
            public short Y;
            private short W;

            public short Z1 { get => (short)(Z * -1); set => Z = value; }
        }

        private struct Normal /*: Vertex we can't inherit struct in C#*/
        {
            public short X;
            private short Z;
            private short Y;
            private short W;

            public short Z1 { get => (short)(Z * -1); set => Z = value; }
        }

#pragma warning restore 169
#pragma warning restore 0649

        #endregion structures

        /// <summary>
        /// This is index to characters in chara.one file of worldmap
        /// </summary>
        public enum worldCharacters
        {
            SquallCasual,
            Ragnarok,
            Chocobo,
            BokoChocobo,
            SquallSeed,
            ZellCasual,
            SelphieCasual
        }

        public static worldCharacterInstance[] worldCharacterInstances = new worldCharacterInstance[8];

        public struct worldCharacterInstance
        {
            public worldCharacters activeCharacter;
            public Vector3 worldPosition;
            public float localRotation;
            public int currentAnimationId;
            public int currentAnimFrame;
            public TimeSpan animationDeltaTime;
            public bool bDraw;
            public float localvRotation;
            public Quaternion localquaternion;
        }

        private static _worldState worldState;
        private static MiniMapState MapState = MiniMapState.rectangle;

        [Flags]
        public enum Texflags : byte
        {
            TEXFLAGS_SHADOW = 0b11,
            TEXFLAGS_UNK = 0b100,
            TEXFLAGS_ISENTERABLE = 0b00001000,
            TEXFLAGS_TRANSPARENT = 0b00010000,
            TEXFLAGS_ROAD = 0b00100000,
            TEXFLAGS_WATER = 0b01000000,
            TEXFLAGS_MISC = 0b10000000
        }

        [Flags]
        private enum VertFlags
        {
            /// <summary>
            /// Player can walk on selected face (and only player)
            /// </summary>
            bWalkable = 0b10000000,

            /// <summary>
            /// Available exclusive to forests and tunnels
            /// </summary>
            bTreeZone = 0b01000000,

            /// <summary>
            /// Marked on faces that are NOT walkable by Player, but are by Chocobo- a thin water for example
            /// </summary>
            bWalkableByChocobo = 0b00010000
        }

        public const byte TRIFLAGS_COLLIDE = 0b10000000;
        public const byte TRIFLAGS_FORESTTEST = 0b01000000;

        private static int GetSegment(int segID) => segID * WM_SEG_SIZE;

        private static void InitWorld()
        {
            fps_camera = new FPS_Camera();
            //init renderer
            effect = new BasicEffect(Memory.Graphics.GraphicsDevice);
            effect.EnableDefaultLighting();
            effect.TextureEnabled = true;
            effect.DirectionalLight0.Enabled = true;
            effect.DirectionalLight1.Enabled = false;
            effect.DirectionalLight2.Enabled = false;
            effect.DirectionalLight0.Direction = new Vector3(
               -0.349999f,
                0.499999f,
                -0.650000f
                );
            effect.DirectionalLight0.SpecularColor = new Vector3(0.8500003f, 0.8500003f, 0.8500003f);
            effect.DirectionalLight0.DiffuseColor = new Vector3(1.54999f, 1.54999f, 1.54999f);
            camTarget = new Vector3(0, 0f, 0f);
            camPosition = new Vector3(-9100.781f, 108.0096f, -4438.435f);
            projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                               MathHelper.ToRadians(cameraFOV),
                               Memory.Graphics.GraphicsDevice.Viewport.AspectRatio,
                1f, 10000f);
            viewMatrix = Matrix.CreateLookAt(camPosition, camTarget,
                         new Vector3(0f, 1f, 0f));// Y up
            //worldMatrix = Matrix.CreateWorld(camTarget, Vector3.
            //              Forward, Vector3.Up);
            worldMatrix = Matrix.CreateTranslation(0, 0, 0);

            if (bUseCustomShaderTest)
            {
                worldShaderModel = Memory.Content.Load<Effect>("testShader");
                worldShaderModel.Parameters["World"].SetValue(worldMatrix);
                worldShaderModel.Parameters["View"].SetValue(viewMatrix);
                worldShaderModel.Parameters["Projection"].SetValue(projectionMatrix);
                worldShaderModel.Parameters["bendValue"].SetValue(BEND_VALUE);
                worldShaderModel.Parameters["bendDistance"].SetValue(BEND_DISTANCE);
                worldShaderModel.Parameters["bendVector"].SetValue(BEND_VECTOR);
                worldShaderModel.Parameters["Projection"].SetValue(projectionMatrix);
                worldShaderModel.Parameters["fogColor"].SetValue(FOG_COLOR);
                worldShaderModel.Parameters["Transparency"].SetValue(1f);
            }
            //temporarily disabling this, because I'm getting more and more tired of this music playing over and over when debugging
            //Memory.musicIndex = 30;
            //AV.Music.Play();
            ate = new AlphaTestEffect(Memory.Graphics.GraphicsDevice)
            {
                Projection = projectionMatrix,
                View = viewMatrix,
                World = worldMatrix,
                FogEnabled = true,
                FogColor = Color.CornflowerBlue.ToVector3(),
                FogStart = 9.75f,
                FogEnd = renderCamDistance
            };

            ReadWorldMapFiles();
            worldCharacterInstances[currentControllableEntity] = new worldCharacterInstance() { activeCharacter = worldCharacters.SquallSeed, worldPosition = playerPosition, localRotation = -90f, currentAnimationId = 0, currentAnimFrame = 0 };
            worldState++;
            return;
        }

        private static void ReadWorldMapFiles()
        {
            var aw = ArchiveWorker.Load(Memory.Archives.A_WORLD);
            var awMain = ArchiveWorker.Load(Memory.Archives.A_MAIN);

            var wmxPath = aw.GetListOfFiles().Where(x => x.ToLower().Contains("wmx.obj")).Select(x => x).First();
            var texlPath = aw.GetListOfFiles().Where(x => x.ToLower().Contains("texl.obj")).Select(x => x).First();
            var wmPath = aw.GetListOfFiles().Where(x => x.ToLower().Contains($"wmset{Extended.GetLanguageShort(true)}.obj")).Select(x => x).First();
            var charaOne = aw.GetListOfFiles().Where(x => x.ToLower().Contains("chara.one")).Select(x => x).First();
            var railFile = aw.GetListOfFiles().Where(x => x.ToLower().Contains("rail.obj")).Select(x => x).First();

            wmx = aw.GetBinaryFile(wmxPath);
            texl = new texl(aw.GetBinaryFile(texlPath));
            chara = new CharaOne(aw.GetBinaryFile(charaOne));
            wmset = new Wmset(aw.GetBinaryFile(wmPath));
            rail = new rail(aw.GetBinaryFile(railFile));

            var wm2fieldPath = awMain.GetListOfFiles().Where(x => x.ToLower().Contains("wm2field.tbl")).Select(x => x).First();

            wm2field = new wm2field(awMain.GetBinaryFile(wm2fieldPath));

            //let's update chara texture indexes due to worldmap VRAM tex atlas behaviour
            chara.AssignTextureSizesForMchInstance(0, new int[] { 0, 1 }); //naturally
            chara.AssignTextureSizesForMchInstance(1, new int[] { 2, 3, 4, 5 }); //ragnarok uses 4 textures!
            for (var i = 2; i < Enum.GetNames(typeof(worldCharacters)).Length; i++)
                chara.AssignTextureSizesForMchInstance(i, new int[] { i * 2 + 2, i * 2 + 3 }); //after ragnarok casual two textures per mesh + two additional due to ragnarok

            segments = new Segment[WM_SEGMENTS_COUNT];

            MemoryStream ms = null;

            using (var br = new BinaryReader(ms = new MemoryStream(wmx)))
            {
                for (var i = 0; i < segments.Length; i++)
                {
                    ms.Seek(GetSegment(i), SeekOrigin.Begin);
                    segments[i] = new Segment { segmentId = i, headerData = Extended.ByteArrayToStructure<SegHeader>(br.ReadBytes(68)), block = new Block[16] };
                    ms.Seek(GetSegment(i), SeekOrigin.Begin);
                    for (var n = 0; n < segments[i].block.Length; n++)
                    {
                        ms.Seek(segments[i].headerData.blockOffsets[n] + GetSegment(i), SeekOrigin.Begin);
                        segments[i].block[n] = new Block { polyCount = br.ReadByte(), vertCount = br.ReadByte(), normalCount = br.ReadByte(), unkPadd = br.ReadByte() };
                        segments[i].block[n].polygons = new Polygon[segments[i].block[n].polyCount];
                        segments[i].block[n].vertices = new Vertex[segments[i].block[n].vertCount];
                        segments[i].block[n].normals = new Normal[segments[i].block[n].normalCount];
                        for (var k = 0; k < segments[i].block[n].polyCount; k++)
                            segments[i].block[n].polygons[k] = Extended.ByteArrayToStructure<Polygon>(br.ReadBytes(16));
                        for (var k = 0; k < segments[i].block[n].vertCount; k++)
                            segments[i].block[n].vertices[k] = Extended.ByteArrayToStructure<Vertex>(br.ReadBytes(8));
                        for (var k = 0; k < segments[i].block[n].normalCount; k++)
                            segments[i].block[n].normals[k] = Extended.ByteArrayToStructure<Normal>(br.ReadBytes(8));
                        segments[i].block[n].unkPadd2 = br.ReadInt32();
                    }
                    var ptd = new List<ParsedTriangleData>();
                    var interI = interchangeableZones.GetInterchangableSegmentReplacementIndex(i);
                    var baseX = 512f * (interI % 32);
                    var baseY = -512f * (interI / 32);
                    for (var n = 0; n < segments[i].block.Length; n++)
                    {
                        float localX = 2048 * (n % 4);
                        float localZ = -2048 * (n / 4);
                        for (var k = 0; k < segments[i].block[n].polyCount; k++)
                        {
                            Vector2[] uvs = {
                                new Vector2(segments[i].block[n].polygons[k].U1 / 256.0f, segments[i].block[n].polygons[k].V1 / 256.0f),
                                new Vector2(segments[i].block[n].polygons[k].U2 / 256.0f, segments[i].block[n].polygons[k].V2 / 256.0f),
                                new Vector2(segments[i].block[n].polygons[k].U3 / 256.0f, segments[i].block[n].polygons[k].V3 / 256.0f)
                                            };
                            if (segments[i].block[n].polygons[k].texFlags.HasFlag(Texflags.TEXFLAGS_ROAD)) //this is roads UV fix
                            {
                                uvs[0] += new Vector2(0f, 0.002f);
                                uvs[1] += new Vector2(0f, 0.002f);
                                uvs[2] += new Vector2(0f, 0.002f);
                            }
                            ptd.Add(new ParsedTriangleData()
                            {
                                A = new Vector3(
                                ((segments[i].block[n].vertices[segments[i].block[n].polygons[k].F1].X + localX) / WORLD_SCALE_MODEL + baseX) * -1f,
                                segments[i].block[n].vertices[segments[i].block[n].polygons[k].F1].Z1 / WORLD_SCALE_MODEL,
                                (segments[i].block[n].vertices[segments[i].block[n].polygons[k].F1].Y + localZ) / WORLD_SCALE_MODEL + baseY),
                                uvA = uvs[0],
                                parentPolygon = segments[i].block[n].polygons[k],
                                B = new Vector3(
                                ((segments[i].block[n].vertices[segments[i].block[n].polygons[k].F2].X + localX) / WORLD_SCALE_MODEL + baseX) * -1f,
                                segments[i].block[n].vertices[segments[i].block[n].polygons[k].F2].Z1 / WORLD_SCALE_MODEL,
                                (segments[i].block[n].vertices[segments[i].block[n].polygons[k].F2].Y + localZ) / WORLD_SCALE_MODEL + baseY),
                                uvB = uvs[1],
                                C = new Vector3(
                                ((segments[i].block[n].vertices[segments[i].block[n].polygons[k].F3].X + localX) / WORLD_SCALE_MODEL + baseX) * -1f,
                                segments[i].block[n].vertices[segments[i].block[n].polygons[k].F3].Z1 / WORLD_SCALE_MODEL,
                                (segments[i].block[n].vertices[segments[i].block[n].polygons[k].F3].Y + localZ) / WORLD_SCALE_MODEL + baseY),
                                uvC = uvs[2]
                            });
                            var ptda = ptd[ptd.Count - 1];
                            ptda.boundingBox = Extended.GetBoundingBox(ptda.A, ptda.B, ptda.C);
                            ptd[ptd.Count - 1] = ptda;
                        }
                    }
                    segments[i].parsedTriangle = ptd.ToArray();
                }
                ms = null;
            }
        }

        public static bool bHasMoved = false;
        public static bool bFirstRun = true;
        public static int currentControllableEntity = 0;

        public static void Update(GameTime deltaTime)
        {
            UpdateTextureAnimation();
            switch (worldState)
            {
                case _worldState._0init:
                    InitWorld();
                    break;

                case _worldState._1active:
                    OrbitCamera();
                    break;

                case _worldState._9debugFly:
                    viewMatrix = fps_camera.Update(ref camPosition, ref camTarget, ref degrees);
                    break;
            }

            InputUpdate();
            CollisionUpdate();
            AnimationUpdate();
            if (bHasMoved || bFirstRun)
            {
                worldCharacterInstances[currentControllableEntity].worldPosition = playerPosition;
                EncounterUpdate();
                bFirstRun = false;
            }
            if (bHasMoved)
            {
                UpdatePlayerQuaternion(ref worldCharacterInstances[currentControllableEntity].localquaternion);
            }
        }

        private static void AnimationUpdate()
        {
            if (bHasMoved)
            {
                if (worldCharacterInstances[currentControllableEntity].activeCharacter == worldCharacters.Ragnarok)
                {
                    //some other anim system
                }
                else
                {
                    worldCharacterInstances[currentControllableEntity].currentAnimationId = 1;
                }
            }
            else
                worldCharacterInstances[currentControllableEntity].currentAnimationId = 0;

            var reverse = false;
            for (var i = 0; i < worldCharacterInstances.Length; i++)
            {
                var instance = worldCharacterInstances[i];
                var flying = currentControllableEntity != i;
                var framesCount = chara.GetMCH((int)instance.activeCharacter).GetAnimationFramesCount(instance.currentAnimationId);

                if (!(instance as worldCharacterInstance?).HasValue)
                    continue;
                if (instance.activeCharacter == worldCharacters.Ragnarok && !flying)
                    reverse = true;

                if (instance.animationDeltaTime >= TimePerFrame)
                {
                    if (reverse)
                        instance.currentAnimFrame--;
                    else
                    {
                        var Frames = (int)(instance.animationDeltaTime.TotalMilliseconds / TimePerFrame.TotalMilliseconds);
                        Frames =  MathHelper.Clamp(Frames, 0, 3);
                        instance.currentAnimFrame += Frames ;
                    }
                    instance.animationDeltaTime = TimeSpan.Zero;
                }
                else instance.animationDeltaTime += Memory.ElapsedGameTime;

                if (instance.activeCharacter == worldCharacters.Ragnarok)
                {
                    if (flying)
                    {
                        if (instance.currentAnimFrame > framesCount - 1)
                            continue;
                    }
                    else
                    {
                        if (instance.currentAnimFrame < 0)
                            continue;
                    }
                }

                if (instance.currentAnimFrame > framesCount - 1)
                    instance.currentAnimFrame = 0;
                else if (instance.currentAnimFrame < 0)
                    instance.currentAnimFrame = checked((int)framesCount - 1);

                worldCharacterInstances[i] = instance;
            }
        }

        private static TimeSpan TimePerFrame => TimeSpan.FromMilliseconds(1000 / 60.0);

        private static void UpdateTextureAnimation()
        {
            if (wmset == null)
                return;
            var beachAnims = wmset.BeachAnimations;
            var waterAnims = wmset.WaterAnimations;
            UpdateTextureAnimation_SelectedStruct(ref beachAnims);
            UpdateTextureAnimation_SelectedStruct(ref waterAnims, true);
            wmset.BeachAnimations = beachAnims;
            wmset.WaterAnimations = waterAnims;
        }

        private static void UpdateTextureAnimation_SelectedStruct(ref Wmset.textureAnimation[] beachAnims, bool bWater = false)
        {
            for (var i = 0; i < beachAnims.Length; i++)
            {
                var totalMaxValue = TimeSpan.FromMilliseconds((15.625f * beachAnims[i].animTimeout)); //1 is 15.625 milliseconds, because 0x20 is 500 milliseconds
                beachAnims[i].deltaTime += Memory.ElapsedGameTime;
                if (beachAnims[i].deltaTime > totalMaxValue)
                {
                    if (beachAnims[i].bIncrementing)
                        beachAnims[i].currentAnimationIndex++;
                    else
                        beachAnims[i].currentAnimationIndex--;
                    beachAnims[i].deltaTime = TimeSpan.Zero;
                    if (beachAnims[i].currentAnimationIndex >= beachAnims[i].framesCount)
                        if (beachAnims[i].bLooping > 0)
                        {
                            beachAnims[i].bIncrementing = !beachAnims[i].bIncrementing;
                            beachAnims[i].currentAnimationIndex = beachAnims[i].framesCount - 2;
                        }
                        else
                            beachAnims[i].currentAnimationIndex = 0;
                    if (beachAnims[i].currentAnimationIndex < 0)
                    {
                        beachAnims[i].currentAnimationIndex = 1;
                        beachAnims[i].bIncrementing = !beachAnims[i].bIncrementing;
                    }
                    if (bWater)
                        wmset.UpdateWorldMapWaterTexturePaletteForAnimation(i, wmset.GetWaterAnimationPalettes(i, beachAnims[i].currentAnimationIndex));
                }
            }
        }

        /// <summary>
        /// If player moved then check for available encounters and if we should play it
        /// </summary>
        private static void EncounterUpdate()
        {
            //RE: if ((world_currentVehicle < 0 || world_currentVehicle > 9) && world_currentVehicle != 128 || !isStateOfMovement   //Naturally, we don't want encounters if in vehicle
            int regionId = wmset.GetWorldRegionBySegmentPosition((int)segmentPosition.X, (int)segmentPosition.Y); //section2
            if (activeCollidePolygon == null)
                return;
            int groundId = activeCollidePolygon.Value.groundtype;
            int encPointer = wmset.GetEncounterHelperPointer(regionId, groundId); //section1
            if (encPointer == 0xffff)
                return;
            var AvailableEncounters = wmset.GetEncounters(encPointer); //section4

            //we now have 8 encounters-> 4 casual; 2 mid and 2 rare

            var encounterRoll = Memory.Random.Next(16 + 4 + 2);
            //casual
            if (encounterRoll < 4) //0123
                Memory.Encounters.ID = AvailableEncounters[0];
            else if (encounterRoll >= 4 && encounterRoll < 8) //4567
                Memory.Encounters.ID = AvailableEncounters[1];
            else if (encounterRoll >= 8 && encounterRoll < 12) //891011
                Memory.Encounters.ID = AvailableEncounters[2];
            else if (encounterRoll >= 12 && encounterRoll < 16) //12131415
                Memory.Encounters.ID = AvailableEncounters[3];
            else if (encounterRoll >= 16 && encounterRoll < 18) //1617
                Memory.Encounters.ID = AvailableEncounters[4];
            else if (encounterRoll >= 18 && encounterRoll < 20) //1819
                Memory.Encounters.ID = AvailableEncounters[5];
            else if (encounterRoll == 20) //20
                Memory.Encounters.ID = AvailableEncounters[6];
            else if (encounterRoll == 21) //21
                Memory.Encounters.ID = AvailableEncounters[7];

            debugEncounter = Memory.Encounters.ID;
            //TODO random + enc.half/none junction + warping to battle
            var state = Memory.State;
        }

        /// <summary>
        /// Convert vector to angle
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        /// <see cref="https://stackoverflow.com/questions/2276855/xna-2d-vector-angles-whats-the-correct-way-to-calculate"/>
        public static float VectorToAngle(Vector2 vector) => (float)Math.Atan2(vector.Y, -vector.X);

        public static float DetectedSpeed;
        private const float playerSpeed = 25f; //the lower the faster

        /// <summary>
        /// Provides 4-axis support for input of currently controlled entity
        /// TODO: extend to 360/ fix diagonal double speed/ calculate local degrees based on sticks
        /// </summary>
        /// <param name="localquaternion"></param>
        private static void InputUpdate()
        {
            if (Input2.Button(InputActions.Cancel))
            {
                Memory.Module = OpenVIII.Module.MainMenuDebug;
                return;
            }
            bHasMoved = false;
            lastPlayerPosition = playerPosition;
            if (Input2.Button(Keys.F1))
                bLockMouse = !bLockMouse;
            if (bLockMouse != true)
            {
                Memory.IsMouseVisible = true;
                return;
            }
            else
                Memory.IsMouseVisible = false;

            if (Input2.DelayedButton(Keys.J) || Input2.DelayedButton(FF8TextTagKey.Select))
                MapState = MapState >= MiniMapState.fullscreen ? MapState = 0 : MapState + 1;

            if (Input2.DelayedButton(Keys.R))
                worldState = _worldState._0init;

            if (Input2.Button(Keys.D9))
                worldState = worldState == _worldState._1active ? _worldState._9debugFly : _worldState._1active;

            if (Input2.Button(Keys.D8))
                bDebugDisableCollision = !bDebugDisableCollision;

            if (MapState == MiniMapState.fullscreen) //FULLSCREEN MAP
            {
                MiniMaps.Input();
            }
            else if (worldState != _worldState._9debugFly)
            {
                var shift = InputGamePad.Distance(GamePadButtons.ThumbSticks_Left, 1f);
                var right = InputGamePad.Distance(GamePadButtons.ThumbSticks_Right, 1f);

                if (right.Y != 0 && worldCharacterInstances[currentControllableEntity].activeCharacter == worldCharacters.Ragnarok)
                {
                    playerPosition.Y += MathHelper.Clamp(right.Y, -10f, 10f) / 10f;
                    bHasMoved = true;
                }
                if (shift != Vector2.Zero)
                {
                    var angle = VectorToAngle(shift);
                    //Debug.WriteLine($"Shift: {shift} Angle: {MathHelper.ToDegrees(angle)} Camera: {degrees}");
                    playerPosition.X += (float)Math.Cos(MathHelper.ToRadians(MathHelper.ToDegrees(angle) + degrees - 90));
                    playerPosition.Z += (float)Math.Sin(MathHelper.ToRadians(MathHelper.ToDegrees(angle) + degrees - 90));
                    bHasMoved = true;
                }
                else
                {
                    // the shift vector in the ifs seemed to give undesired results.
                    if (Input2.Button(FF8TextTagKey.Up)/* || shift.Y > 0*/)
                    {
                        playerPosition.X += (float)Math.Cos(MathHelper.ToRadians(degrees));
                        playerPosition.Z += (float)Math.Sin(MathHelper.ToRadians(degrees));
                        //localRotation = (float)Extended.Radians(-degrees - 90f);
                        bHasMoved = true;
                    }
                    else if (Input2.Button(FF8TextTagKey.Down)/* || shift.Y < 0*/)
                    {
                        playerPosition.X -= (float)Math.Cos(MathHelper.ToRadians(degrees));
                        playerPosition.Z -= (float)Math.Sin(MathHelper.ToRadians(degrees));
                        //localRotation = (float)Extended.Radians(-degrees + 90f);
                        bHasMoved = true;
                    }
                    if (Input2.Button(FF8TextTagKey.Left) /*|| shift.X < 0*/)
                    {
                        playerPosition.X += (float)Math.Cos(MathHelper.ToRadians(degrees - 90f));
                        playerPosition.Z += (float)Math.Sin(MathHelper.ToRadians(degrees - 90f));
                        //localRotation = (float)Extended.Radians(-degrees);
                        bHasMoved = true;
                    }
                    else if (Input2.Button(FF8TextTagKey.Right)/* || shift.X > 0*/)
                    {
                        playerPosition.X += (float)Math.Cos(MathHelper.ToRadians(degrees + 90f));
                        playerPosition.Z += (float)Math.Sin(MathHelper.ToRadians(degrees + 90f));
                        //localRotation = (float)Extended.Radians(180f - degrees);
                        bHasMoved = true;
                    }
                }
                if (bHasMoved)
                {
                    var diffvect = (playerPosition - lastPlayerPosition); // gets the vector between the old and new calculated pos.
                    diffvect.Normalize(); //prevents speed up from going in more than one direction.
                    // this will slow your movement based on stick.
                    //not best for squall but maybe for vehicles
                    if (shift != Vector2.Zero && InVehicle)
                    {
                        const float distmax = 10f;
                        var dist = MathHelper.Clamp(Vector2.Distance(Vector2.Zero, shift), 0f, distmax);
                        //Debug.WriteLine($"Dist: {dist}, DistMax: {distmax}");
                        diffvect *= dist / distmax;
                    }
                    playerPosition = lastPlayerPosition + (diffvect * (float)(Memory.ElapsedGameTime.TotalMilliseconds/ playerSpeed));
                }
            }
            if (Input2.Button(Keys.F3))
            {
                Menu.BattleMenus.CameFrom(); // allows returning to current state after victory menu complete.
                Extended.postBackBufferDelegate = BattleSwirl.Init;
                Extended.RequestBackBuffer();
            }
        }

        private static void UpdatePlayerQuaternion(ref Quaternion localquaternion)
        {
            if (playerPosition != lastPlayerPosition)
            {
                DetectedSpeed = Vector3.Distance(playerPosition, lastPlayerPosition);

                float yaw = 0f, pitch = 0f, roll = 0f;
                //https://www.codeproject.com/Questions/324240/Determining-yaw-pitch-and-roll
                var matrix = Matrix.CreateLookAt(lastPlayerPosition, playerPosition, Vector3.Up);
                yaw = (float)Math.Atan2(matrix.M13, matrix.M33);
                pitch = (float)Math.Asin(-matrix.M23);
                //roll = (float)Math.Atan2(matrix.M21, matrix.M22);
                //yaw = (float)Math.Atan2(-diffvect.X, -diffvect.Z); //this seems to make squall face the correct direction each time.
                //pitch = (float)Math.Atan2(diffvect.Y, Math.Abs(diffvect.X)); // unsure if this is prefect or not.

                if (yaw == 0f) yaw = worldCharacterInstances[currentControllableEntity].localRotation;
                else
                    worldCharacterInstances[currentControllableEntity].localRotation = yaw;

                localquaternion = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
                worldCharacterInstances[currentControllableEntity].localvRotation = pitch;
            }
            else
                DetectedSpeed = 0f;
        }

        /// <summary>
        /// ParsedTriangleData- struct contains all available data paired with found triangle Vector3
        /// - contains barycentric based on playerPosition bool - bIsSkyRaycasted - used for sky raycast
        /// </summary>
        private static List<RayCastedTris> RaycastedTris;

        /// <summary>

        /// This method checks for collision- uses raycasting and 3Dintersection to either allow
        /// movement, update it and/or warp player. If all checks fails it returns to last known
        /// correct player position This points to polygon structure that is actively used/ character
        /// stomps on it </summary>
        public static Polygon? activeCollidePolygon = null;

        public static int GetRealSegmentId() => (int)(segmentPosition.Y * 32 + segmentPosition.X); //explicit public for wmset and warping sections

        public static int GetRealSegmentId(float x, float y) => (int)((y < 0 ? 24 + y : y) * 32 + (x < 0 ? 32 + x : x)); //explicit public for wmset and warping sections

        public struct RayCastedTris
        {
            public ParsedTriangleData data;
            public Vector3 pos;
            public bool sky;

            public RayCastedTris(ParsedTriangleData data, Vector3 pos, bool sky)
            {
                this.data = data;
                this.pos = pos;
                this.sky = sky;
            }
        }

        private static float MinY
        {
            get
            {
                var worldCharacterInstance = worldCharacterInstances[currentControllableEntity];
                switch (worldCharacterInstance.activeCharacter)
                {
                    case worldCharacters.Ragnarok:
                        return (20 - worldCharacterInstance.currentAnimFrame) * .5f;

                    default:
                        return 0f;
                }
            }
        }

        private static string CollisionStringDebug;
        /// <summary>
        /// This method checks for collision- uses raycasting and 3Dintersection to either allow
        /// movement, update it and/or warp player. If all checks fails it returns to last known
        /// correct player position
        /// </summary>
        private static void CollisionUpdate()
        {
            segmentPosition = new Vector2((int)(playerPosition.X / 512) * -1, (int)(playerPosition.Z / 512) * -1); //needs to be updated on pre-new values of movement

            var realSegmentId = GetRealSegmentId();
            realSegmentId = interchangeableZones.SetInterchangeableZone(realSegmentId);
            var seg = segments[realSegmentId];
            RaycastedTris = new List<RayCastedTris>();
            var position = playerPosition + new Vector3(0, 15f, 0);
            var characterRay = new Ray(position, Vector3.Down); //sets ray origin
            var characterRay2 = new Ray(position, Vector3.Up); //sets ray origin

            var skyRay = new Ray(GetForwardSkyRaycastVector(SKYRAYCAST_FIXEDDISTANCE), Vector3.Down);

            //loop through current block triangles - two rays at the same time. There are only two rays and multi triangles, so iterate triangles and check rays instead of double checking
            for (var i = 0; i < seg.parsedTriangle.Length; i++)
                if (Extended.RayIntersection3D(characterRay, seg.parsedTriangle[i].A, seg.parsedTriangle[i].B, seg.parsedTriangle[i].C, out var characterBarycentric) != 0)
                    RaycastedTris.Add(new RayCastedTris(seg.parsedTriangle[i], characterBarycentric, false));
                // There are spots where you can fly under the map by like flying into the ground or
                // a corner. This would put the ship back above around.
                else if (BDebugDisableCollision && Extended.RayIntersection3D(characterRay2, seg.parsedTriangle[i].A, seg.parsedTriangle[i].B, seg.parsedTriangle[i].C, out var characterBarycentric2) != 0)
                    RaycastedTris.Add(new RayCastedTris(seg.parsedTriangle[i], characterBarycentric2, false));
                else if (Extended.RayIntersection3D(skyRay, seg.parsedTriangle[i].A, seg.parsedTriangle[i].B, seg.parsedTriangle[i].C, out var skyBarycentric) != 0)
                    RaycastedTris.Add(new RayCastedTris(seg.parsedTriangle[i], skyBarycentric, true));

            //don't allow walking over non-walkable faces - just because we tested both rays we can make this linq appear only once
            if (!BDebugDisableCollision)
                RaycastedTris = OrderAndCheckTrisForcollisionsBetween().Where(x => (x.data.parentPolygon.vertFlags & TRIFLAGS_COLLIDE) != 0 && x.pos != Vector3.Zero).ToList();

            //WORLD MAP TO FIELD CHECK- it should be done on already walked polygon so the player will be able to
            //enter the triangle with warp zone and warp AFTER that, not just as he enters it
            WorldMapToField();

            const float forestAdj = -12f;//-12f;
            foreach (var prt in RaycastedTris)
            {
                if (prt.sky) //we do not want skyRaycasts here, iterate only characterRay
                    continue;
                var distance = playerPosition - prt.pos;
                var distY = Math.Abs(distance.Y);
                if (distY >= 15f && !InVehicle) // prevents walking off a clifft most of the time.
                    continue;
                if ((prt.data.parentPolygon.vertFlags & TRIFLAGS_FORESTTEST) != 0)
                    MinYPos(prt.pos);
                else
                {
                    MinYPos(prt.pos, forestAdj);
                }

                activeCollidePolygon = prt.data.parentPolygon;
                return;
            }

            //out of loop- failed to obtain collision or abandon move - we need to check now if player wanted to get to forest
            foreach (var prt in RaycastedTris)
            {
                if (!prt.sky) //we do not want skyRaycasts here, iterate only characterRay
                    continue;
                //we do not want to check for Y here
                if ((prt.data.parentPolygon.vertFlags & TRIFLAGS_FORESTTEST) != 0) //this opts out non-forest faces
                    continue;
                MinYPos(prt.pos, forestAdj);
                activeCollidePolygon = prt.data.parentPolygon;
                return;
            }




            if (!BDebugDisableCollision)
                playerPosition = lastPlayerPosition;
        }

        private static void WorldMapToField()
        {
            if (activeCollidePolygon != null)
                if (activeCollidePolygon.Value.texFlags.HasFlag(Texflags.TEXFLAGS_ISENTERABLE))
                {
                    foreach (var warpZone in wmset.section8WarpZones)
                    {
                        var fieldId = wm2field.GetFieldId(warpZone.field);
                        var bShouldWarp = true;
                        if (warpZone.segmentId != GetRealSegmentId())
                            continue;
                        if (imguiStrings == null)
                            imguiStrings = new List<string>();
                        imguiStrings.Add("WARPZONE!");
                        imguiStrings.Add("---------");
                        imguiStrings.Add($"fieldId: {fieldId}");
                        imguiStrings.Add($"First segmentId: {warpZone.segmentId}");
                        foreach (var condition in warpZone.conditions)
                        {
                            //test conditions here, so far we don't really know them much enough
                            //for example fire cavern is on the same segment as Balamb, so there's additional check with
                            //the player position. The Fullscreen map is also created by section8 (probably)
                        }
                        if (bShouldWarp)
                        {
                            for (var contId = 0; contId < warpZone.conditions.Length; contId++)
                                imguiStrings.Add($"{contId}: {warpZone.conditions[contId].opcode}({warpZone.conditions[contId].opcode.ToString("X")}):{warpZone.conditions[contId].argument}");

                            //    Fields.Module.ResetField();
                            //Memory.FieldHolder.FieldID = (ushort)fieldId;
                            //Memory.Module = MODULE.FIELD_DEBUG;
                        }
                        activeCollidePolygon = null; //invalidate current polygon so you won't warp twice when field2wm
                                                     //invalidating activecollidepolygon is not enough- set the position too by wmset.section9
                    }
                }
        }

        private static RayCastedTris? CameraCollisionUpdate()
        {
            segmentPosition = new Vector2((int)(camPosition.X / 512) * -1, (int)(camPosition.Z / 512) * -1); //needs to be updated on pre-new values of movement
            var realSegmentId = (int)(segmentPosition.Y * 32 + segmentPosition.X);
            realSegmentId = interchangeableZones.SetInterchangeableZone(realSegmentId);
            var seg = segments[realSegmentId];
            RaycastedTris = new List<RayCastedTris>();
            var skyRay = new Ray(new Vector3(camPosition.X, 5000f, camPosition.Z), Vector3.Down); //drops ray at camPosition from sky
            for (var i = 0; i < seg.parsedTriangle.Length; i++)
                if (Extended.RayIntersection3D(skyRay, seg.parsedTriangle[i].A, seg.parsedTriangle[i].B, seg.parsedTriangle[i].C, out var skyBarycentric) != 0)
                    RaycastedTris.Add(new RayCastedTris(seg.parsedTriangle[i], skyBarycentric, true));

            //take care of sky rays only
            if (RaycastedTris.Count == 0)
                return null;
            else return RaycastedTris[0];
        }

        private static List<RayCastedTris> OrderAndCheckTrisForcollisionsBetween()
        {
            // Order Tris by distance to player
            var ordered = RaycastedTris.OrderBy(x => Vector3.Distance(playerPosition, x.pos)).ToList();
            var between = false;
            // Check to see if there is a collision between the player and the next walkable space.
            // prevent jumping over collision.
            for (var i = 0; ordered.Count > i; i++)
            {
                if (ordered[i].sky) continue;
                //var d = Vector3.Distance(playerPosition, ordered[i].pos);
                var collide = (ordered[i].data.parentPolygon.vertFlags & TRIFLAGS_COLLIDE) != 0;
                if (collide)
                    between = true;
                else if (between)
                { //bassically if there is collision between mark rest to collide.
                    //if (bHasMoved)
                    //Debug.WriteLine(d + "\t");
                    var x = ordered[i];
                    x.data.parentPolygon.vertFlags |= TRIFLAGS_COLLIDE;
                    ordered[i] = x;
                }
            }

            return ordered;
        }

        private static void MinYPos(Vector3 squaPos, float adj = 0f)
        {
            if (worldCharacterInstances[currentControllableEntity].activeCharacter != worldCharacters.Ragnarok || playerPosition.Y < squaPos.Y + MinY)
            {
                //Force character to min Y elivation.
                playerPosition.Y = squaPos.Y + MinY + adj;

                //This smooths out the drop down.Though this would only trigger while moving.
                // So would need to move this or check collision while not moving.so commented out.

                //Vector3 min = (squaPos + new Vector3(0, MinY + adj, 0));
                //if (min.Y > playerPosition.Y)
                //    playerPosition.Y = min.Y;
                //else if (Vector3.Distance(squaPos, playerPosition) > 1)
                //{
                //    Vector3 adjv = (playerPosition - min) * (-1f);
                //    adjv.Normalize();
                //    playerPosition.Y += adjv.Y;
                //}
            }
        }

        /// <summary>
        /// This is the relative distance that is added to forward vector of character and then
        /// casted from sky to bottom of the level
        /// </summary>
        private const float SKYRAYCAST_FIXEDDISTANCE = 2f;

        private const float RotationInterval = 1.5f;


        private static bool bLockMouse = true;

        private static int orbitCameraMode = 1; //parse from save file [TODO]
        private static float camSlider = 0f;
        private static bool camSliderDirection = false;
        private static float rememberedCamCollideHeight = 0f;
        public static void OrbitCamera()
        {
            if (bLockMouse)
                InputMouse.Mode = MouseLockMode.Center;
            else
                InputMouse.Mode = MouseLockMode.Disabled;
            if (Input2.Button(Keys.F, ButtonTrigger.OnPress)) //[TODO]
            {
                orbitCameraMode = orbitCameraMode == 0 ? 1 : 0;
                camSliderDirection = orbitCameraMode == 0;
            }
            if (camSliderDirection)
                camSlider += 1f * (float)(Memory.ElapsedGameTime.Milliseconds / 1000f);
            else
                camSlider -= 1f * (float)(Memory.ElapsedGameTime.Milliseconds / 1000f);
            camSlider = MathHelper.Clamp(camSlider, 0f, 1f);

            switch (orbitCameraMode)
            {
                case 0: //closeup
                    camHeight = MathHelper.Lerp(200f, 100f + rememberedCamCollideHeight, camSlider);
                    cameraFOV = MathHelper.Lerp(42f, 44f, camSlider);
                    var activepoly = CameraCollisionUpdate();
                    if (activepoly.HasValue)
                        rememberedCamCollideHeight = activepoly.Value.pos.Y;
                    break;
                case 1: //faraway
                    camHeight = MathHelper.Lerp(200f, 100f + rememberedCamCollideHeight, camSlider);
                    cameraFOV = MathHelper.Lerp(42, 44, camSlider);
                    break;
            }
            camPosition = new Vector3(
                (float)(playerPosition.X + camDistance * Extended.Cos(degrees - 180f)),
                camHeight,//playerPosition.Y + 50f,
                (float)(playerPosition.Z + camDistance * Extended.Sin(degrees - 180f))
                );
            // check mouse to adjust camera
            var shift = InputMouse.Distance(MouseButtons.MouseToStick, FPS_Camera.maxLookSpeedMouse);
            // check right stick to adjust camera

            var rightstick = InputGamePad.Distance(GamePadButtons.ThumbSticks_Right, FPS_Camera.maxLookSpeedGamePad);
            //Debug.WriteLine($"{rightstick.X}");
            shift += rightstick;
            if (Input2.Button(FF8TextTagKey.RotateLeft, ButtonTrigger.Press | ButtonTrigger.IgnoreDelay))
                degrees -= RotationInterval * (float)Memory.ElapsedGameTime.TotalMilliseconds/25f;
            if (Input2.Button(FF8TextTagKey.RotateRight, ButtonTrigger.Press | ButtonTrigger.IgnoreDelay))
                degrees += RotationInterval *(float)Memory.ElapsedGameTime.TotalMilliseconds / 25f;
            degrees += shift.X;
            degrees %= 360f;
            if (degrees < 0)
            {
                degrees += 360f;
            }
            camTarget = new Vector3(playerPosition.X, 50f, playerPosition.Z);
            projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                   MathHelper.ToRadians(cameraFOV),
                   Memory.Graphics.GraphicsDevice.Viewport.AspectRatio,
    1f, 10000f);
            viewMatrix = Matrix.CreateLookAt(camPosition, camTarget,
                         Vector3.Up);
        }

        private double RadianAngleFromVector3s(Vector3 a, Vector3 b) => Math.Acos(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)));


        static Color bgGradient = Color.CornflowerBlue;
        static List<string> imguiStrings;
        static bool bImguiSec8 = false;
        static bool bImguiSec9 = false;
        static bool bImguiSec11 = false;

        public static void Draw()
        {
            Memory.SpriteBatch.GraphicsDevice.Clear(bgGradient);



            Memory.Graphics.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            Memory.Graphics.GraphicsDevice.BlendState = BlendState.NonPremultiplied;
            Memory.Graphics.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            Memory.Graphics.GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
            ate.Projection = projectionMatrix;
            ate.View = viewMatrix;
            ate.World = worldMatrix;
            effect.Projection = projectionMatrix;
            effect.View = viewMatrix;
            effect.World = worldMatrix;

            if (bUseCustomShaderTest)
            {
                worldShaderModel.Parameters["Projection"].SetValue(ate.Projection);
                worldShaderModel.Parameters["View"].SetValue(ate.View);
                worldShaderModel.Parameters["World"].SetValue(ate.World);
                worldShaderModel.Parameters["camWorld"].SetValue(camPosition);
                worldShaderModel.Parameters["skyColor"].SetValue(skyColor);
            }


            segmentPosition = new Vector2((int)(playerPosition.X / 512) * -1, (int)(playerPosition.Z / 512) * -1);
            if (segmentPosition.Y == 24)
                segmentPosition.Y = 23;
            if (segmentPosition.X == 32)
                segmentPosition.X = 31;

            //let's get segments ids for cube
            /*
             * SEG0 SEG1 SEG2
             * SEG3 CURR SEG5
             * SEG6 SEG7 SEG8 */

            DrawSegment(-1, -1); //SEG0
            DrawSegment(0, -1); //SEG1
            DrawSegment(1, -1); //SEG2
            DrawSegment(-1, 0); //SEG3
            DrawSegment(0, 0); //draws current walkable segment
            DrawSegment(+1, 0); //SEG5
            DrawSegment(-1, +1); //SEG6
            DrawSegment(0, +1); //SEG7
            DrawSegment(+1, +1); //SEG8

            if (degrees < 90 || degrees > 270)
            {
                DrawSegment(-2, 0);
                DrawSegment(-2, 1);
                DrawSegment(-2, -1);
            }
            if (degrees < 360 && degrees > 180)
            {
                DrawSegment(0, 2);
                DrawSegment(-1, 2);
                DrawSegment(1, 2);
            }
            if (degrees < 180 && degrees > 0)
            {
                DrawSegment(0, -2);
                DrawSegment(-1, -2);
                DrawSegment(+1, -2);
                if (segmentPosition.X == 31 && segmentPosition.Y == 0) //fix for the bahamuth underwater place was not visible at specific situations
                    DrawSegment(+2, -2);
            }
            if (degrees < 270 || degrees > 90)
            {
                DrawSegment(2, 0);
                DrawSegment(2, 1);
                DrawSegment(2, -1);
            }

            TeleportPlayerWarp();
            CloudRender.DrawBackgroundClouds();

            foreach (var charaInstance in worldCharacterInstances)
                DrawCharacter(charaInstance);
            DrawDebug_Rays();
            SpecialEffectsRenderer.DrawCharacterShadowSpecialEffects();

            switch (MapState)
            {
                case MiniMapState.noMinimap:
                    break;

                case MiniMapState.planet:
                    MiniMaps.DrawPlanetMiniMap();
                    break;

                case MiniMapState.rectangle:
                    MiniMaps.DrawRectangleMiniMap();
                    break;

                case MiniMapState.fullscreen:
                    MiniMaps.DrawFullScreenMap();
                    break;
            }


            var playerangle = MathHelper.ToDegrees(worldCharacterInstances[currentControllableEntity].localRotation);
            if (playerangle < 0) playerangle += 360f;
            ImguiDebugDraw(playerangle);
        }

        private static void ImguiDebugDraw(float playerangle)
        {
            if (Memory.GameTime != null)
                Memory.ImGui.BeforeLayout(Memory.GameTime);
            ImGuiNET.ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero, ImGuiNET.ImGuiCond.Once);
            ImGuiNET.ImGui.SetNextWindowBgAlpha(.25f);
            ImGuiNET.ImGui.Begin("WORLD DEBUG");
            ImGuiNET.ImGui.Text($"Press F1 to lock or unlock mouse: ={bLockMouse}");
            ImGuiNET.ImGui.Text($"Press 8 to enable/disable collision: ={bDebugDisableCollision}");
            ImGuiNET.ImGui.Text($"Press 9 to enable debug FPS camera: ={(worldState == _worldState._1active ? "orbit camera" : "FPS debug camera")}");
            ImGuiNET.ImGui.InputFloat("Debug variable:", ref debugVar);
            ImGuiNET.ImGui.Separator();
            var imgui_skyColor = new System.Numerics.Vector4(bgGradient.R / 255f, bgGradient.G / 255f, bgGradient.B / 255f, bgGradient.A / 255f); //redundancy hell
            ImGuiNET.ImGui.ColorEdit4("Sky color: ", ref imgui_skyColor);
            var imgui_skyColor2 = new System.Numerics.Vector4(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
            ImGuiNET.ImGui.ColorEdit4("Colorize: ", ref imgui_skyColor2);
            skyColor = new Vector3(imgui_skyColor2.X, imgui_skyColor2.Y, imgui_skyColor2.Z);
            bgGradient = new Color(imgui_skyColor.X, imgui_skyColor.Y, imgui_skyColor.Z, imgui_skyColor.W);
            ImGuiNET.ImGui.Text($"World map MapState: ={MapState}");
            var imgui_cameraPosition = new System.Numerics.Vector3(camPosition.X, camPosition.Y, camPosition.Z);
            ImGuiNET.ImGui.InputFloat3("World map camera", ref imgui_cameraPosition);
            camPosition = new Vector3(imgui_cameraPosition.X, imgui_cameraPosition.Y, imgui_cameraPosition.Z);
            ImGuiNET.ImGui.InputFloat("World map camera distance", ref camDistance);
            ImGuiNET.ImGui.InputFloat("World map camera height", ref camHeight);
            ImGuiNET.ImGui.InputFloat("World map camera FOV", ref cameraFOV);
            ImGuiNET.ImGui.Text($"selWalk2: ={(activeCollidePolygon.HasValue ? activeCollidePolygon.Value.ToString() : "N/A")}");
            ImGuiNET.ImGui.Text($"Camera mode: {orbitCameraMode}");
            ImGuiNET.ImGui.Text($"Camera slide: {camSlider}");
            var imgui_playerPosition = new System.Numerics.Vector3(playerPosition.X, playerPosition.Y, playerPosition.Z);
            ImGuiNET.ImGui.InputFloat3("Player position: ", ref imgui_playerPosition);
            playerPosition = new Vector3(imgui_playerPosition.X, imgui_playerPosition.Y, imgui_playerPosition.Z);
            ImGuiNET.ImGui.Text($"Player rotation: ={playerangle}°");
            ImGuiNET.ImGui.Text($"Player speed: ={DetectedSpeed} units per update");
            ImGuiNET.ImGui.Text($"Segment Position: ={segmentPosition} ({GetSegmentVectorPlayerPosition()})");
            ImGuiNET.ImGui.Text($"encounter: ={debugEncounter}- Press F3 to force battle");
            ImGuiNET.ImGui.Text($"FOV: {FOV}");
            ImGuiNET.ImGui.Text($"1000/deltaTime milliseconds: {(Memory.ElapsedGameTime.TotalSeconds > 0 ? 1d / Memory.ElapsedGameTime.TotalSeconds : 0d)}");
            ImGuiNET.ImGui.Text($"imgui::FPS {ImGuiNET.ImGui.GetIO().Framerate}");
            ImGuiNET.ImGui.Checkbox("Field2WM WARPLIST", ref bImguiSec9);
            ImGuiNET.ImGui.Checkbox("wmset11 locations", ref bImguiSec11);
            ImGuiNET.ImGui.Separator();
            if (imguiStrings != null)
            {
                foreach (var s in imguiStrings)
                    ImGuiNET.ImGui.Text(s);
                imguiStrings.Clear();
            }

            ImGuiNET.ImGui.Separator();
            if (bImguiSec9)
            {
                ImGuiNET.ImGui.Text("-Field2WM-");
                for (var x = 0; x < wmset.fieldToWorldMapLocations.Length; x++)
                {
                    ImGuiNET.ImGui.Text(
                        $"{x}: X={wmset.fieldToWorldMapLocations[x].X}  Y={wmset.fieldToWorldMapLocations[x].Y}  Z={wmset.fieldToWorldMapLocations[x].Z}");
                    ImGuiNET.ImGui.SameLine();
                    if (ImGuiNET.ImGui.Button($"WARP {x}"))
                    {
                        playerPosition.X = wmset.fieldToWorldMapLocations[x].X;
                        playerPosition.Y = wmset.fieldToWorldMapLocations[x].Y;
                        playerPosition.Z = wmset.fieldToWorldMapLocations[x].Z;
                    }
                }
                ImGuiNET.ImGui.Separator();
            }
            if (bImguiSec11)
            {
                ImGuiNET.ImGui.Text("-Section11-");
                for (var x = 0; x < wmset.sec11Locations.Length; x++)
                {
                    ImGuiNET.ImGui.Text(
                        $"{x}: X={wmset.sec11Locations[x].X}  Y={wmset.sec11Locations[x].Y}  Z={wmset.sec11Locations[x].Z}");
                    ImGuiNET.ImGui.SameLine();
                    if (ImGuiNET.ImGui.Button($"WARP s{x}"))
                    {
                        playerPosition.X = wmset.sec11Locations[x].X;
                        playerPosition.Y = wmset.sec11Locations[x].Y;
                        playerPosition.Z = wmset.sec11Locations[x].Z;
                    }
                }
            }


            if (ImGuiNET.ImGui.CollapsingHeader("fullscreenmap debugger"))
            {
                MiniMaps.imgui();
            }
            Texture2D imguiTex = null;
            if (ImGuiNET.ImGui.CollapsingHeader("wmset textures"))
            {
                var enumValues = Enum.GetValues(typeof(Wmset.Section38_textures));
                for (var i = 0; i < enumValues.Length; i++)
                {
                    ImGuiNET.ImGui.Text($"{(Wmset.Section38_textures)i}");
                    ImGuiNET.ImGui.SameLine();
                    imguiTex = (Texture2D)wmset.GetWorldMapTexture(
                        (Wmset.Section38_textures)enumValues.GetValue(i), 0);
                    ImGuiNET.ImGui.Image(Memory.ImGui.BindTexture(imguiTex), new System.Numerics.Vector2(64, 64));
                    if (ImGuiNET.ImGui.IsItemHovered())
                    {
                        ImGuiNET.ImGui.BeginTooltip();
                        ImGuiNET.ImGui.Text($"W: {imguiTex.Width} H: {imguiTex.Height}");
                        ImGuiNET.ImGui.Image(Memory.ImGui.BindTexture(imguiTex), new System.Numerics.Vector2(imguiTex.Width, imguiTex.Height));
                        ImGuiNET.ImGui.End();
                    }
                }
            }
            if (ImGuiNET.ImGui.CollapsingHeader("wmset33"))
            {
                for (var i = 0; i < wmset.skyColors.Length; i++)
                {
                    var col = wmset.skyColors[i].GetLocation();
                    ImGuiNET.ImGui.Text($"sec33: {i}={col}");
                    ImGuiNET.ImGui.SameLine();
                    if (ImGuiNET.ImGui.Button($"sec33WARP{i}"))
                    {
                        playerPosition = col;
                        var color = wmset.skyColors[i].GetShadowsColor();
                        Vector3 colorVec = new Vector3(color.R, color.G, color.B);
                        colorVec.Normalize();
                        skyColor = colorVec;
                    }
                    ImGuiNET.ImGui.Text($"{i}= shadow {wmset.skyColors[i].GetShadowsColor()}");
                    ImGuiNET.ImGui.Text($"{i}= vehicle {wmset.skyColors[i].GetVehiclesColor()}");
                    ImGuiNET.ImGui.Text($"{i}= topBG {wmset.skyColors[i].GetTopBGColor()}");
                    ImGuiNET.ImGui.Text($"{i}= centerBG {wmset.skyColors[i].GetCenterBGColor()}");
                    ImGuiNET.ImGui.Text($"{i}= bottomBG {wmset.skyColors[i].GetBottomBGColor()}");
                    ImGuiNET.ImGui.Text($"{i}= 1- {wmset.skyColors[i].unk1_1} {wmset.skyColors[i].unk1_2} {wmset.skyColors[i].unk1_3} {wmset.skyColors[i].unk1_4}");
                    ImGuiNET.ImGui.Text($"{i}= 2- {wmset.skyColors[i].unk2_1} {wmset.skyColors[i].unk2_2} {wmset.skyColors[i].unk2_3} {wmset.skyColors[i].unk2_4}");
                    ImGuiNET.ImGui.Text($"{i}= 3- {wmset.skyColors[i].unk3_1} {wmset.skyColors[i].unk3_2} {wmset.skyColors[i].unk3_3} {wmset.skyColors[i].unk3_4}");
                }
            }
            //ImGuiNET.ImGui.Begin("!Texture lister!");
            //ImGuiNET.ImGui.Image(Memory.imgui.BindTexture((Texture2D)wmset.GetWorldMapTexture(wmset.Section38_textures.minimapFullScreenPointer, 0)),
            //    new System.Numerics.Vector2(64,64));
            //ImGuiNET.ImGui.End();
            ImGuiNET.ImGui.End();
            Memory.ImGui.AfterLayout();
        }

        private static void DrawDebug_VehiclePreview()
        {
            var localTranslation = Module_world_debug.playerPosition + new Vector3(20f, 10f, 20f);
            for (var i = 0; i < Module_world_debug.wmset.GetVehicleModelsCount(); i++)
            {
                var vehTex = (Texture2D)Module_world_debug.wmset.GetVehicleTexture(i, 0);
                var originVector = Module_world_debug.wmset.GetVehicleTextureOriginVector(i, 0);
                var dMod = Module_world_debug.wmset.GetVehicleGeometry(i, localTranslation + Vector3.Left * 50f * i, Quaternion.Identity, new Vector2(vehTex.Width, vehTex.Height), originVector);
                for (var n = 0; n < dMod.Item1.Length; n += 3)
                {
                    Module_world_debug.ate.Texture = (Texture2D)Module_world_debug.wmset.GetVehicleTexture(i, 0);
                    foreach (var pass in Module_world_debug.ate.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, dMod.Item1, n, 1);
                    }
                }
            }
        }

        private static void DrawDebug_Rays()
        {
            var playerRaycastDownVerts = new[] { new VertexPositionColor(playerPosition, Color.White), new VertexPositionColor(new Vector3(playerPosition.X, -1, playerPosition.Z), Color.White) };
            var skyRaycastDownVerts = GetForwardSkyRaycastVector(SKYRAYCAST_FIXEDDISTANCE);
            var skyVectorDropVerts = new[]
            {
                new VertexPositionColor(skyRaycastDownVerts, Color.White), //draw line from mockup up to the bottom fake infinity
                new VertexPositionColor(new Vector3(skyRaycastDownVerts.X, -5000f, skyRaycastDownVerts.Z), Color.White)
            };

            if (RaycastedTris.Count != 0)
                foreach (var tt in RaycastedTris)
                {
                    var triangle = tt.data;
                    var verts2 = new[] {new VertexPositionColor(triangle.A, Color.White),
                new VertexPositionColor(triangle.B, Color.White),

                new VertexPositionColor(triangle.B, Color.White),
                new VertexPositionColor(triangle.C, Color.White),

                new VertexPositionColor(triangle.C, Color.White),
                new VertexPositionColor(triangle.A, Color.White)
                };
                    foreach (var pass in ate.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, verts2, 0, 3);
                    }
                }

            foreach (var pass in ate.CurrentTechnique.Passes)
            {
                pass.Apply();
                Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, playerRaycastDownVerts, 0, 1);
                Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, skyVectorDropVerts, 0, 1);
            }
        }

        private static float GetSegmentVectorPlayerPosition() => segmentPosition.Y * 32 + segmentPosition.X;



        /// <summary>
        /// Gets the vector3 position of the raycast that drops from sky and is used for forest
        /// </summary>
        /// <returns></returns>
        private static Vector3 GetForwardSkyRaycastVector(float distance = 5f)
        {
            var playerangle = MathHelper.ToDegrees(worldCharacterInstances[currentControllableEntity].localRotation);
            var degreesRadians = (float)Extended.Radians(playerangle + 180f); //gets radians value of current degrees
            var relativeTranslation = new Vector3(
                (float)Math.Sin(degreesRadians) * distance,
                5000f,
                (float)Math.Cos(degreesRadians) * distance
                );
            return new Vector3(playerPosition.X + relativeTranslation.X,
                relativeTranslation.Y,
                playerPosition.Z + relativeTranslation.Z);
        }
        /// <summary>
        /// translates the world map model so it's vertices are drawn as close to playerPosition
        /// vector as possible
        /// </summary>
        private static Vector3 localMchTranslation = new Vector3(0, 6f, 0);

        private static Vector2 Scale;

        public static bool InVehicle => worldCharacterInstances[currentControllableEntity].activeCharacter == worldCharacters.Ragnarok;
        private static bool bDebugDisableCollision = false;
        private static string collisionString;

        public static bool BDebugDisableCollision { get => bDebugDisableCollision || worldCharacterInstances[currentControllableEntity].activeCharacter == worldCharacters.Ragnarok; set => bDebugDisableCollision = value; }

        private static void DrawCharacter(worldCharacterInstance? charaInstance_)
        {
            if (!charaInstance_.HasValue)
                return;
            if (!charaInstance_.Value.bDraw)
                return;
            var charaInstance = charaInstance_.Value;
            var MchIndex = (int)charaInstance.activeCharacter;
            if (charaInstance.currentAnimationId >= chara.GetMCH(MchIndex).GetAnimationCount())
                charaInstance.currentAnimationId = 0;
            var charaCollection = chara.GetMCH(MchIndex).GetVertexPositions(charaInstance.worldPosition + localMchTranslation, charaInstance.localquaternion /*Quaternion.CreateFromYawPitchRoll(charaInstance.localRotation, charaInstance.localvRotation, 0f)*/, charaInstance.currentAnimationId, charaInstance.currentAnimFrame);

            int textureIndexBase; //chara.one contains textures one-by-one but mch indexes are based from zero for each character. That's why we have to sum texIndexes from previous meshes
            switch (charaInstance.activeCharacter)
            {
                case worldCharacters.Ragnarok:
                    textureIndexBase = 2;
                    break;

                case worldCharacters.Chocobo:
                    textureIndexBase = 6;
                    break;

                case worldCharacters.BokoChocobo:
                    textureIndexBase = 8;
                    break;

                case worldCharacters.SquallSeed:
                    textureIndexBase = 10;
                    break;

                case worldCharacters.ZellCasual:
                    textureIndexBase = 12;
                    break;

                case worldCharacters.SelphieCasual:
                    textureIndexBase = 14;
                    break;

                case worldCharacters.SquallCasual:
                default:
                    textureIndexBase = 0;
                    break;
            }

            var vptCollection = new Dictionary<Texture2D, List<VertexPositionColorTexture>>();
            for (var i = 0; i < charaCollection.Item2.Length; i += 3)
            {
                var charaTexture = chara.GetCharaTexture(textureIndexBase + charaCollection.Item2[i]);
                if (!vptCollection.ContainsKey(charaTexture))
                    vptCollection.Add(charaTexture, new List<VertexPositionColorTexture>());
                vptCollection[charaTexture].AddRange(charaCollection.Item1.Skip(i).Take(3).ToArray());
            }

            foreach (var kvp in vptCollection)
            {
                ate.Texture = kvp.Key;
                if (bUseCustomShaderTest)
                {
                    worldShaderModel.Parameters["ModelTexture"].SetValue(ate.Texture);
                    worldShaderModel.CurrentTechnique = worldShaderModel.Techniques["Texture_fog_bend"];
                }
                foreach (var pass in bUseCustomShaderTest ? worldShaderModel.CurrentTechnique.Passes : ate.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, kvp.Value.ToArray(), 0, kvp.Value.Count / 3);
                }
            }
        }

        /// <summary>
        /// This prevents camera/player to get out of playable zone and wraps it to the other side
        /// like it's 360o
        /// </summary>
        private static void TeleportPlayerWarp()
        {
            if (playerPosition.X > 0)
                playerPosition.X = 32 * 512 * -1;
            if (playerPosition.X < 32 * 512 * -1)
                playerPosition.X = 0;

            if (playerPosition.Z > 0)
                playerPosition.Z = 24 * 512 * -1;
            if (playerPosition.Z < 24 * 512 * -1)
                playerPosition.Z = 0;
        }

        private static void DrawSegment(int xTranslation, int yTranslation)
        {
            effect.TextureEnabled = true;
            var _i = GetRealSegmentId((segmentPosition.X + xTranslation) % 32, (segmentPosition.Y + yTranslation) % 24);

            _i = interchangeableZones.SetInterchangeableZone(_i);

            var seg = segments[_i];
            var translationVector = Vector3.Zero;
            var playerSegmentVector = segmentPosition;

            if (playerSegmentVector.X + xTranslation < 0)
                translationVector = new Vector3(32 * 512, 0, 0); //LEFT
            if (playerSegmentVector.Y + yTranslation < 0)
                translationVector = new Vector3(0, 0, 24 * 512); //UP

            if (playerSegmentVector.X + xTranslation > 31)
                translationVector = new Vector3(32 * -512, 0, 0); //RIGHT
            if (playerSegmentVector.Y + yTranslation > 23)
                translationVector = new Vector3(0, 0, 24 * -512); //BOTTOM

            if (playerSegmentVector.X + xTranslation < 0 && playerSegmentVector.Y + yTranslation < 0 && xTranslation < 0 && yTranslation < 0) //UL diagonal wrap
                translationVector = new Vector3(32 * 512, 0, 24 * 512);
            if (playerSegmentVector.X + xTranslation > 31 && playerSegmentVector.Y + yTranslation < 0 && xTranslation > 0 && yTranslation < 0) //UR diagonal wrap
                translationVector = new Vector3(32 * -512, 0, 24 * 512);
            if (playerSegmentVector.X + xTranslation > 31 && playerSegmentVector.Y + yTranslation > 23 && xTranslation > 0 && yTranslation > 0) //BR diagonal wrap
                translationVector = new Vector3(32 * -512, 0, 24 * -512);
            if (playerSegmentVector.X + xTranslation < 0 && playerSegmentVector.Y + yTranslation > 23 && xTranslation < 0 && yTranslation > 0) //BL diagonal wrap
                translationVector = new Vector3(32 * 512, 0, 24 * -512);

            Dictionary<Texture2D, Tuple<List<VertexPositionTexture>, bool>> groupedPolygons = new Dictionary<Texture2D, Tuple<List<VertexPositionTexture>, bool>>();
            Dictionary<Texture2D, Tuple<List<VertexPositionTexture>, bool>> transparentGroupedPolygons = new Dictionary<Texture2D, Tuple<List<VertexPositionTexture>, bool>>();

            for (var k = 0; k < seg.parsedTriangle.Length; k++)
            {
                var firstEdge = seg.parsedTriangle[k].A + translationVector;
                var faceDistance = Extended.Distance3D(playerPosition, firstEdge);
                if (faceDistance > renderCamDistance) //this face is beyond the rendering zone; ignore whole segment!
                    continue;
                if (CheckFrustrumView(firstEdge.X, firstEdge.Z))
                    continue;

                var parsedTriangleB = seg.parsedTriangle[k].B + translationVector;
                var parsedTriangleC = seg.parsedTriangle[k].C + translationVector;
                var bIsWaterBlock = false;

                var vpc = new VertexPositionTexture[3];
                vpc[0] = new VertexPositionTexture(
                    firstEdge,
                    seg.parsedTriangle[k].uvA);
                vpc[1] = new VertexPositionTexture(
                    parsedTriangleB,
                    seg.parsedTriangle[k].uvB);
                vpc[2] = new VertexPositionTexture(
                    parsedTriangleC,
                    seg.parsedTriangle[k].uvC);
                var poly = seg.parsedTriangle[k].parentPolygon;
                if (poly.texFlags.HasFlag(Texflags.TEXFLAGS_ROAD))
                    ate.Texture = wmset.GetRoadsMiscTextures();
                else if (poly.texFlags.HasFlag(Texflags.TEXFLAGS_WATER))
                {
                    SetWaterAnimationTexture(seg, k, vpc, poly);
                    bIsWaterBlock = true;
                }
                else
                    ate.Texture = (Texture2D)texl.GetTexture(poly.TPage, poly.Clut);

                if (poly.texFlags.HasFlag(Texflags.TEXFLAGS_TRANSPARENT))
                {
                    if (transparentGroupedPolygons.ContainsKey(ate.Texture))
                        transparentGroupedPolygons[ate.Texture].Item1.AddRange(vpc);
                    else
                        transparentGroupedPolygons.Add(ate.Texture, new Tuple<List<VertexPositionTexture>, bool>(new List<VertexPositionTexture>() { vpc[0], vpc[1], vpc[2] }, bIsWaterBlock));
                }
                else
                {
                    if (groupedPolygons.ContainsKey(ate.Texture))
                        groupedPolygons[ate.Texture].Item1.AddRange(vpc);
                    else
                        groupedPolygons.Add(ate.Texture, new Tuple<List<VertexPositionTexture>, bool>(new List<VertexPositionTexture>() { vpc[0], vpc[1], vpc[2] }, bIsWaterBlock));
                }
            }

            //I hate to do so much redundancy here, but that's dictionary lookup, also it's important to draw important stuff
            //at the very end after opaque triangles
            foreach (var kvp in groupedPolygons) //normal draw
            {
                ate.Texture = kvp.Key;
                VertexPositionTexture[] vptFinal = kvp.Value.Item1.ToArray();
                ate.Alpha = 1f;
                if (bUseCustomShaderTest)
                {
                    worldShaderModel.Parameters["ModelTexture"].SetValue(ate.Texture);
                    worldShaderModel.Parameters["Transparency"].SetValue(1f);
                }

                if (kvp.Value.Item2 && bUseCustomShaderTest)
                    worldShaderModel.CurrentTechnique = worldShaderModel.Techniques["Texture_fog_bend_waterAnim"];
                else if (bUseCustomShaderTest)
                    worldShaderModel.CurrentTechnique = worldShaderModel.Techniques["Texture_fog_bend"];

                foreach (var pass in bUseCustomShaderTest ? worldShaderModel.CurrentTechnique.Passes : ate.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vptFinal, 0, vptFinal.Length / 3);
                }
            }

            foreach (var kvp in transparentGroupedPolygons) //transparent draw
            {
                ate.Texture = kvp.Key;
                VertexPositionTexture[] vptFinal = kvp.Value.Item1.ToArray();
                ate.Alpha = 0.5f;
                if (bUseCustomShaderTest)
                {
                    worldShaderModel.Parameters["ModelTexture"].SetValue(ate.Texture);
                    worldShaderModel.Parameters["Transparency"].SetValue(0.5f);
                }

                if (kvp.Value.Item2 && bUseCustomShaderTest)
                    worldShaderModel.CurrentTechnique = worldShaderModel.Techniques["Texture_fog_bend_waterAnim"];
                else if (bUseCustomShaderTest)
                    worldShaderModel.CurrentTechnique = worldShaderModel.Techniques["Texture_fog_bend"];

                foreach (var pass in bUseCustomShaderTest ? worldShaderModel.CurrentTechnique.Passes : ate.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    Memory.Graphics.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vptFinal, 0, vptFinal.Length / 3);
                }
            }

        }

        private static void SetWaterAnimationTexture(Segment seg, int k, VertexPositionTexture[] vpc, Polygon poly)
        {
            /*
             *  GP=10 - Beach [ANIMATED]
                GP=31/CLUT0 - river endflow
                GP=31/CLUT6 - waterfall [ANIMATED]
                GP=31/CLUT3 - river [ANIMATED]
                GP=32 - Thin water- walkable with chocobo
                GP=33 - transition between thin water to ocean
                GP=34 - ocean
                */
            var waterAtlas = wmset.GetWorldMapWaterTexture();
            if (poly.groundtype == 10 || poly.groundtype == 32 || (poly.groundtype == 31 && poly.Clut == 3)) //BEACH + flat water + river flowing down
            {
                var @as = seg.parsedTriangle[k].parentPolygon;
                var animationIdPointer = 1; //beach corner
                if (@as.Clut == 2)
                    animationIdPointer = 0; //beach atlas
                if (@as.Clut == 3 && poly.groundtype == 31)
                    animationIdPointer = 2; //river anim

                var texx = wmset.GetBeachAnimationTextureFrame(animationIdPointer, wmset.BeachAnimations[animationIdPointer].currentAnimationIndex);
                var Ucoorder = @as.Clut == 2 ? 128f : 192;
                var Vcoorder = @as.Clut == 3 && poly.groundtype == 31 ? 32f : 0f;
                if (poly.groundtype == 10 || (poly.groundtype == 32 && poly.Clut == 2) || (poly.groundtype == 31 && poly.Clut == 3))
                {
                    vpc[0].TextureCoordinate = new Vector2((@as.U1 - Ucoorder) / texx.Width, (@as.V1 - Vcoorder) / texx.Height);
                    vpc[1].TextureCoordinate = new Vector2((@as.U2 - Ucoorder) / texx.Width, (@as.V2 - Vcoorder) / texx.Height);
                    vpc[2].TextureCoordinate = new Vector2((@as.U3 - Ucoorder) / texx.Width, (@as.V3 - Vcoorder) / texx.Height);
                }

                if (poly.groundtype == 10 || (poly.groundtype == 32 && poly.Clut == 2) || (poly.groundtype == 31 && poly.Clut == 3))
                    ate.Texture = wmset.GetBeachAnimationTextureFrame(animationIdPointer, wmset.BeachAnimations[animationIdPointer].currentAnimationIndex);
                else if (poly.groundtype == 32)
                {
                    vpc[0].TextureCoordinate = new Vector2(@as.U1 / (float)waterAtlas.Width, @as.V1 / (float)waterAtlas.Height);
                    vpc[1].TextureCoordinate = new Vector2(@as.U2 / (float)waterAtlas.Width, @as.V2 / (float)waterAtlas.Height);
                    vpc[2].TextureCoordinate = new Vector2(@as.U3 / (float)waterAtlas.Width, @as.V3 / (float)waterAtlas.Height);
                    ate.Texture = waterAtlas;
                }
            }
            else if (Extended.In(poly.groundtype, 31, 34))
            {
                var @as = seg.parsedTriangle[k].parentPolygon;
                vpc[0].TextureCoordinate = new Vector2(@as.U1 / (float)waterAtlas.Width, @as.V1 / (float)waterAtlas.Height);
                vpc[1].TextureCoordinate = new Vector2(@as.U2 / (float)waterAtlas.Width, @as.V2 / (float)waterAtlas.Height);
                vpc[2].TextureCoordinate = new Vector2(@as.U3 / (float)waterAtlas.Width, @as.V3 / (float)waterAtlas.Height);
                ate.Texture = waterAtlas;
            }
            else
                ate.Texture = (Texture2D)wmset.GetWorldMapTexture(Wmset.Section38_textures.waterTex2, 0); //FAIL- should not be used (I think)
        }

        /// <summary>
        /// This method checks if it should not draw some faces based on frustum culling method of
        /// checking point in triangle (2D geometry)
        /// </summary>
        /// <param name="pointX">X coordinate</param>
        /// <param name="pointY">Y (actually Z from 3D) coordinate</param>
        /// <returns></returns>
        private static bool CheckFrustrumView(float pointX, float pointY)
        {
            float ax, ay, d1, d2, d3;
            ax = camPosition.X + (float)Math.Cos(MathHelper.ToRadians(degrees)) * -100f;
            ay = camPosition.Z + (float)Math.Sin(MathHelper.ToRadians(degrees)) * -100f;

            Vector3 left = Vector3.Zero, right = Vector3.Zero;
            left.X = camPosition.X + (float)Math.Cos(MathHelper.ToRadians(Extended.ClampOverload(degrees - FOV, 0, 359))) * renderCamDistance * 2;
            left.Z = camPosition.Z + (float)Math.Sin(MathHelper.ToRadians(Extended.ClampOverload(degrees - FOV, 0, 359))) * renderCamDistance * 2;
            right.X = camPosition.X + (float)Math.Cos(MathHelper.ToRadians(Extended.ClampOverload(degrees + FOV, 0, 359))) * renderCamDistance * 2;
            right.Z = camPosition.Z + (float)Math.Sin(MathHelper.ToRadians(Extended.ClampOverload(degrees + FOV, 0, 359))) * renderCamDistance * 2;

            d1 = pointX * (ay - left.Z) + pointY * (left.X - ax) + (ax * left.Z - ay * left.X);
            d2 = pointX * (left.Z - right.Z) + pointY * (right.X - left.X) + (left.X * right.Z - left.Z * right.X);
            d3 = pointX * (right.Z - ay) + pointY * (ax - right.X) + (right.X * ay - right.Z * ax);

            return ((d1 > 0 || d2 > 0 || d3 > 0) && (d1 < 0 || d2 < 0 || d3 < 0));
        }
    }
}
