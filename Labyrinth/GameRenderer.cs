﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using Labyrinth.Gui;

namespace Labyrinth {
    class GameRenderer : GameWindowLayer {
        private Game Game;
        private Random Rand = new Random();

        private int TicksCounter = 0;

        public enum CameraMode {
            FirstPerson,
            ThirdPerson
        };
        public CameraMode Camera = CameraMode.FirstPerson;

        private const float WallsHeight = 1.0f;
        private const float WallsHeightVariation = 0.1f;
        private const float WallsXyVariation = 0.1f;
        private const float IconMinSize = 0.35f, IconMaxSize = 0.40f;
        private const float VisibilityDistance = 7f;
        private const float GhostSize = 0.7f;

        private Texture TextureWall, TextureExit, TextureKey, TextureMark, TextureGhostSide, TextureGhostTop;
        private Color4[] CheckpointsColors = { Color4.OrangeRed, Color4.Aquamarine, Color4.DodgerBlue, Color4.Yellow };
        private int GhostFramesCount, GhostFrame = 0;
        private Text WinLabel;

        private delegate void RenderDelegate(object Data);
        private struct TransparentObjectsBufferRecord {
            public Vector2 Position;
            public object Data;
            public RenderDelegate Render;

            public TransparentObjectsBufferRecord(Vector2 Position, object Data, RenderDelegate Render) {
                this.Position = Position;
                this.Data = Data;
                this.Render = Render;
            }
        }
        private List<TransparentObjectsBufferRecord> TransparentObjectsBuffer = new List<TransparentObjectsBufferRecord>(10);
        private struct IconData {
            public Position Position;
            public Texture Texture;
            public Color4 Color;

            public IconData(Position Position, Texture Texture, Color4 Color) {
                this.Position = Position;
                this.Texture = Texture;
                this.Color = Color;
            }
        }

        private float TorchLight = 0;
        private float TorchLightChangeDirection = -1;

        private int? FadeOutStarted = null;
        private const int FadeOutLength = 42;

        public GameRenderer(GameWindow Window, Game Game)
            : base(Window) {
            this.Game = Game;

            TextureWall = new Texture(new Bitmap("textures/wall.png"));
            TextureExit = new Texture(new Bitmap("textures/exit.png"));
            TextureKey = new Texture(new Bitmap("textures/key.png"));
            TextureMark = new Texture(new Bitmap("textures/mark.png"));
            TextureGhostSide = new Texture(new Bitmap("textures/ghost.png"));
            TextureGhostTop = new Texture(new Bitmap("textures/ghost-from-top.png"));

            GhostFramesCount = (int)(TextureGhostSide.Width / TextureGhostSide.Height);

            WinLabel = new Text("You win!");
            WinLabel.Color = Color4.White;
            WinLabel.Font = new Font(new FontFamily(GenericFontFamilies.SansSerif), 50, GraphicsUnit.Pixel);
        }

        public override void Tick() {
            ++TicksCounter;

            TorchLight = Game.TorchLight;
            if (Game.StateEnum.Playing == Game.State) {
                var TorchLightChangeMin = 0.05f * TorchLight;
                var TorchLightChangeMax = 0.10f * TorchLight;
                var TorchLightChange = (float)Rand.NextDouble() * (TorchLightChangeMax - TorchLightChangeMin) + TorchLightChangeMin;

                TorchLight = TorchLight + TorchLightChange * TorchLightChangeDirection;
                TorchLight = Math.Max(TorchLight, 0);
                TorchLight = Math.Min(TorchLight, 100);

                if (Rand.Next(100) < 10) {
                    TorchLightChangeDirection = -TorchLightChangeDirection;
                }
            }

            if (Rand.Next(100) < 5) {
                GhostFrame = Rand.Next(GhostFramesCount);
            }
        }

        public override void Render() {
            GL.Enable(EnableCap.DepthTest);

            var Projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 4, Window.Width / (float)Window.Height, 1e-3f, VisibilityDistance * 2);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref Projection);

            var Modelview = Matrix4.LookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref Modelview);

            var PlayerPosition3d = new Vector3(Game.Player.Position.X, Game.Player.Position.Y, 0);
            var VariousedPosition = VariousedPoint(PlayerPosition3d);
            PlayerPosition3d.Z = VariousedPosition.Z + Game.Player.Size.Z;

            if (CameraMode.ThirdPerson == Camera) {
                GL.Rotate(60, Vector3.UnitX); // look down
                GL.Translate(0, 2, 0); // stay behind
                GL.Rotate(Game.Player.Angle, Vector3.UnitZ);
                GL.Translate(Vector3.Multiply(PlayerPosition3d, -1f));
                GL.Translate(0, 0, -WallsHeight * 3); // fly higher
            } else {
                GL.Rotate(Game.Player.Angle, Vector3.UnitZ);
                GL.Translate(Vector3.Multiply(PlayerPosition3d, -1f));
            }

            GL.Enable(EnableCap.Lighting);
            if (TorchLight > 0) {
                var TorchPosition = new Vector4(Game.Player.Position);
                TorchPosition.W = 1;

                GL.Enable(EnableCap.Light0);
                GL.Light(LightName.Light0, LightParameter.Position, TorchPosition);
                GL.Light(LightName.Light0, LightParameter.ConstantAttenuation, 1 / (0.18f * TorchLight + 1.82f));
                GL.Light(LightName.Light0, LightParameter.Ambient, Color4.SaddleBrown);
                GL.Light(LightName.Light0, LightParameter.Diffuse, Color4.SaddleBrown);
                GL.Light(LightName.Light0, LightParameter.Specular, Color4.SaddleBrown);
            } else {
                GL.Disable(EnableCap.Light0);
            }

            GL.Enable(EnableCap.Fog);
            GL.Fog(FogParameter.FogDensity, (CameraMode.ThirdPerson != Camera) ? 0.5f : 0.1f);

            GL.Enable(EnableCap.Texture2D);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            TransparentObjectsBuffer.Clear();

            RenderMap();

            TransparentObjectsBuffer.Add(new TransparentObjectsBufferRecord(
                Game.Map.FinishPosition,
                new IconData(Game.Map.FinishPosition, TextureExit, (Game.CollectedCheckpoints.Count == Game.Map.Checkpoints.Count) ? Color4.ForestGreen : Color4.Red),
                new RenderDelegate(RenderIcon)
            ));

            for (var i = 0; i < Game.Map.Checkpoints.Count; i++) {
                if (!Game.CollectedCheckpoints.Contains(i)) {
                    TransparentObjectsBuffer.Add(new TransparentObjectsBufferRecord(
                        Game.Map.Checkpoints[i], 
                        new IconData(Game.Map.Checkpoints[i], TextureKey, CheckpointsColors[i]), 
                        new RenderDelegate(RenderIcon)
                    ));
                }
            }

            foreach (var Mark in Game.Marks) {
                TransparentObjectsBuffer.Add(new TransparentObjectsBufferRecord(Mark, new IconData(Mark, TextureMark, Color4.MediumOrchid), new RenderDelegate(RenderIcon)));
            }

            foreach (var Ghost in Game.Ghosts) {
                TransparentObjectsBuffer.Add(new TransparentObjectsBufferRecord(Ghost.Position, Ghost, new RenderDelegate(RenderGhost)));
            }

            RenderTransparentObjects();

            if (CameraMode.ThirdPerson == Camera) {
                RenderPlayer();
            }

            if (Game.StateEnum.Win == Game.State) {
                RenderWinScreen();
            }
        }

        private void RenderMap() {
            GL.PushAttrib(AttribMask.TextureBit);

            TextureWall.Bind();

            GL.Begin(BeginMode.Quads);

            var Xmin = (int)Math.Floor(Math.Max(Game.Player.Position.X - VisibilityDistance, 0));
            var Ymin = (int)Math.Floor(Math.Max(Game.Player.Position.Y - VisibilityDistance, 0));
            var Xmax = (int)Math.Ceiling(Math.Min(Game.Player.Position.X + VisibilityDistance, Game.Map.Width - 1));
            var Ymax = (int)Math.Ceiling(Math.Min(Game.Player.Position.Y + VisibilityDistance, Game.Map.Height - 1));

            for (var X = Xmin; X <= Xmax; X++) {
                for (var Y = Ymin; Y <= Ymax; Y++) {
                    var Position = new Vector2(X, Y);

                    if (Map.CellType.Empty == Game.Map.GetCell(X, Y)) {
                        if (Map.CellType.Wall == Game.Map.GetCell(X, Y + 1)) {
                            RenderWall(new Vector2(X, Y + 1), new Vector2(X + 1, Y + 1));
                        }
                        if (Map.CellType.Wall == Game.Map.GetCell(X, Y - 1)) {
                            RenderWall(new Vector2(X + 1, Y), new Vector2(X, Y));
                        }
                        if (Map.CellType.Wall == Game.Map.GetCell(X - 1, Y)) {
                            RenderWall(new Vector2(X, Y), new Vector2(X, Y + 1));
                        }
                        if (Map.CellType.Wall == Game.Map.GetCell(X + 1, Y)) {
                            RenderWall(new Vector2(X + 1, Y + 1), new Vector2(X + 1, Y));
                        }

                        RenderFloor(Position);
                        if (CameraMode.FirstPerson == Camera) {
                            RenderCeiling(Position);
                        }
                    }
                }
            }

            GL.End();

            GL.PopAttrib();
        }

        private Vector3 VariousedPoint(Vector3 Position) {
            var Result = Position;
            Result.X += (float)Math.Sin((Position.X + Position.Y + Position.Z + Math.E) * 1.5) * WallsXyVariation;
            Result.Y += (float)Math.Sin((Position.X + Position.Y + Position.Z + Math.E) * 2.5) * WallsXyVariation;
            Result.Z += (float)Math.Sin((Position.X + Position.Y + Position.Z + Math.E) * 1.0) * WallsHeightVariation;
            return Result;
        }

        private Vector3 VariousedPoint(float X, float Y, float Z) {
            return VariousedPoint(new Vector3(X, Y, Z));
        }

        private void RenderWall(Vector2 A, Vector2 B) {
            var C = new Vector2((A.X + B.X) / 2f, (A.Y + B.Y) / 2f); // middlepoint
            Vector2[] P1 = { A, C };
            Vector2[] P2 = { C, B };

            for (var i = 0; i < 2; i++) {
                var M = P1[i];
                var N = P2[i];
                for (float Z = 0; Z < WallsHeight; Z += WallsHeight / 2) {
                    GL.TexCoord2(M.Equals(A) ? 0 : 0.5, Z + WallsHeight / 2); GL.Vertex3(VariousedPoint(M.X, M.Y, Z + WallsHeight / 2));
                    GL.TexCoord2(M.Equals(A) ? 0.5 : 1, Z + WallsHeight / 2); GL.Vertex3(VariousedPoint(N.X, N.Y, Z + WallsHeight / 2));
                    GL.TexCoord2(M.Equals(A) ? 0.5 : 1, Z); GL.Vertex3(VariousedPoint(N.X, N.Y, Z));
                    GL.TexCoord2(M.Equals(A) ? 0 : 0.5, Z); GL.Vertex3(VariousedPoint(M.X, M.Y, Z));
                }
            }
        }

        private void RenderFloor(Vector2 P) {
            for (var X = P.X; X <= P.X + 0.5; X += 0.5f) {
                for (var Y = P.Y; Y <= P.Y + 0.5; Y += 0.5f) {
                    GL.TexCoord2(X - P.X, Y - P.Y); GL.Vertex3(VariousedPoint(X, Y, 0));
                    GL.TexCoord2(X - P.X + 0.5, Y - P.Y); GL.Vertex3(VariousedPoint(X + 0.5f, Y, 0));
                    GL.TexCoord2(X - P.X + 0.5, Y - P.Y + 0.5); GL.Vertex3(VariousedPoint(X + 0.5f, Y + 0.5f, 0));
                    GL.TexCoord2(X - P.X, Y - P.Y + 0.5); GL.Vertex3(VariousedPoint(X, Y + 0.5f, 0));
                }
            }
        }

        private void RenderCeiling(Vector2 P) {
            for (var X = P.X; X <= P.X + 0.5; X += 0.5f) {
                for (var Y = P.Y; Y <= P.Y + 0.5; Y += 0.5f) {
                    GL.TexCoord2(X - P.X, Y - P.Y); GL.Vertex3(VariousedPoint(X, Y, WallsHeight));
                    GL.TexCoord2(X - P.X + 0.5, Y - P.Y); GL.Vertex3(VariousedPoint(X + 0.5f, Y, WallsHeight));
                    GL.TexCoord2(X - P.X + 0.5, Y - P.Y + 0.5); GL.Vertex3(VariousedPoint(X + 0.5f, Y + 0.5f, WallsHeight));
                    GL.TexCoord2(X - P.X, Y - P.Y + 0.5); GL.Vertex3(VariousedPoint(X, Y + 0.5f, WallsHeight));
                }
            }
        }

        private void RenderIcon(object RecordData) {
            var Data = (IconData)RecordData;

            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.PushMatrix();

            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Fog);

            GL.Translate(Data.Position.X + 0.5, Data.Position.Y + 0.5, WallsHeight / 2);
            GL.Rotate(-Game.Player.Angle, Vector3.UnitZ);
            if (CameraMode.FirstPerson != Camera) {
                GL.Rotate(-90, Vector3.UnitX);
            }

            var Size = (IconMaxSize - IconMinSize) / 2 * (Math.Sin(TicksCounter / 10f) / 2 - 1) + IconMaxSize;

            Data.Texture.Bind();
            GL.Color4(Data.Color.R, Data.Color.G, Data.Color.B, 0.7f);

            GL.Begin(BeginMode.Quads);
            GL.TexCoord2(0, 1); GL.Vertex3(-Size / 2, 0, -Size / 2);
            GL.TexCoord2(0, 0); GL.Vertex3(-Size / 2, 0, Size / 2);
            GL.TexCoord2(1, 0); GL.Vertex3(Size / 2, 0, Size / 2);
            GL.TexCoord2(1, 1); GL.Vertex3(Size / 2, 0, -Size / 2);
            GL.End();

            GL.PopMatrix();
            GL.PopAttrib();
        }

        private int CompareBufferedTransparentObjects(TransparentObjectsBufferRecord A, TransparentObjectsBufferRecord B) {
            var DistanceA = (A.Position - Game.Player.Position).Length;
            var DistanceB = (B.Position - Game.Player.Position).Length;
            return DistanceA < DistanceB ? -1 : +1; // using (int)(DistanceA - DistanceB) will lead to unexpected rounding problems
        }

        private void RenderTransparentObjects() {
            TransparentObjectsBuffer.Sort(CompareBufferedTransparentObjects);
            TransparentObjectsBuffer.Reverse();
            foreach (var Object in TransparentObjectsBuffer) {
                Object.Render(Object.Data);
            }
        }

        private void RenderGhost(object RecordData) {
            var Ghost = (Ghost)RecordData;

            var TextureWidth = 1 / (float)GhostFramesCount;
            var TextureX = (GhostFrame - 1) * TextureWidth;

            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.PushMatrix();

            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Fog);

            GL.Translate(Ghost.Position.X + 0.5, Ghost.Position.Y + 0.5, WallsHeight / 2);

            GL.Color4(new Color4(1f, 1f, 1f, 1f));

            if (CameraMode.FirstPerson == Camera) {
                GL.Rotate(-Game.Player.Angle, Vector3.UnitZ);

                TextureGhostSide.Bind();

                GL.Begin(BeginMode.QuadStrip);

                GL.TexCoord2(TextureX, 0);
                GL.Vertex3(-GhostSize / 2, +GhostSize / 2, +GhostSize / 2);
                GL.TexCoord2(TextureX, 1);
                GL.Vertex3(-GhostSize / 2, +GhostSize / 2, -GhostSize / 2);

                GL.TexCoord2(TextureX + TextureWidth * 0.25, 0);
                GL.Vertex3(-GhostSize / 4, 0, +GhostSize / 2);
                GL.TexCoord2(TextureX + TextureWidth * 0.25, 1);
                GL.Vertex3(-GhostSize / 4, 0, -GhostSize / 2);

                GL.TexCoord2(TextureX + TextureWidth * 0.75, 0);
                GL.Vertex3(+GhostSize / 4, 0, +GhostSize / 2);
                GL.TexCoord2(TextureX + TextureWidth * 0.75, 1);
                GL.Vertex3(+GhostSize / 4, 0, -GhostSize / 2);

                GL.TexCoord2(TextureX + TextureWidth, 0);
                GL.Vertex3(+GhostSize / 2, +GhostSize / 2, +GhostSize / 2);
                GL.TexCoord2(TextureX + TextureWidth, 1);
                GL.Vertex3(+GhostSize / 2, +GhostSize / 2, -GhostSize / 2);

                GL.End();
            } else {
                GL.Rotate(-90, Vector3.UnitX);

                TextureGhostTop.Bind();

                GL.Begin(BeginMode.Quads);

                GL.TexCoord2(TextureX, 1);
                GL.Vertex3(-GhostSize / 2, 0, -GhostSize / 2);

                GL.TexCoord2(TextureX, 0);
                GL.Vertex3(-GhostSize / 2, 0, GhostSize / 2);

                GL.TexCoord2(TextureX + TextureWidth, 0);
                GL.Vertex3(GhostSize / 2, 0, GhostSize / 2);

                GL.TexCoord2(TextureX + TextureWidth, 1);
                GL.Vertex3(GhostSize / 2, 0, -GhostSize / 2);

                GL.End();
            }

            GL.PopAttrib();
            GL.PopMatrix();
        }

        private void RenderPlayer() {
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.PushMatrix();

            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Lighting);

            var PlayerPosition3d = new Vector3(Game.Player.Position.X, Game.Player.Position.Y, 0);
            var VariousedPosition = VariousedPoint(PlayerPosition3d);
            PlayerPosition3d.Z = VariousedPosition.Z + Game.Player.Size.Z;

            GL.Translate(PlayerPosition3d);
            GL.Rotate(-Game.Player.Angle, Vector3.UnitZ);

            GL.Color4(1.0f, 0, 0, 0.7f);

            GL.Begin(BeginMode.Polygon);
            GL.Vertex2(0, Game.Player.Size.Y / 2);
            GL.Vertex2(Game.Player.Size.X / 2, -Game.Player.Size.Y / 2);
            GL.Vertex2(0, -Game.Player.Size.Y / 4);
            GL.Vertex2(-Game.Player.Size.X / 2, -Game.Player.Size.Y / 2);
            GL.End();

            GL.PopMatrix();
            GL.PopAttrib();
        }

        private void RenderWinScreen() {
            if (!FadeOutStarted.HasValue) {
                FadeOutStarted = TicksCounter;
            }
            var FadeOutCounter = TicksCounter - FadeOutStarted.Value;

            GL.PushAttrib(AttribMask.AllAttribBits);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Lighting);

            var Projection = Matrix4.CreateOrthographic(-(float)Window.Width, -(float)Window.Height, -1, 1);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref Projection);
            GL.Translate(Window.Width / 2, -Window.Height / 2, 0);

            var Modelview = Matrix4.LookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref Modelview);

            GL.Color4(new Color4(0, 0, 0, (float)Math.Min(FadeOutCounter, FadeOutLength) / FadeOutLength));

            GL.Begin(BeginMode.Quads);
            GL.Vertex2(0, 0);
            GL.Vertex2(Window.Width, 0);
            GL.Vertex2(Window.Width, Window.Height);
            GL.Vertex2(0, Window.Height);
            GL.End();

            if (FadeOutCounter >= FadeOutLength) {
                WinLabel.Left = (Window.Width - WinLabel.Width.Value) / 2;
                WinLabel.Top = (Window.Height - WinLabel.Height.Value) / 2;
                WinLabel.Render();
            }

            GL.PopAttrib();
        }
    }
}