﻿using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace Labyrinth {
    class Game : GameWindowLayer {
        private Random Rand = new Random();

        public enum DifficultyLevel {
            Easy,
            Normal,
            Hard,
        };

        public Map Map;
        public Player Player;
        public List<int> CollectedCheckpoints = new List<int>();
        public int MarksLeft = 10;
        public List<Position> Marks = new List<Position>();
        public Ghost[] Ghosts;
        public float TorchLight; // 0 (darkness) … 100 (max. light)

        public const float TorchLightLifeTime = 300; // seconds

        public enum StateEnum {
            Playing,
            Win
        }
        public StateEnum State;

        private int TicksCounter;

        private GameRenderer Renderer;

        public Game(GameWindow Window, DifficultyLevel Difficulty)
            : base(Window) {
            if (DifficultyLevel.Easy == Difficulty) {
                Map = new Map(Rand, 10, 10, 2);
                Ghosts = new Ghost[1];
            } else if (DifficultyLevel.Normal == Difficulty) {
                Map = new Map(Rand, 20, 20, 3);
                Ghosts = new Ghost[3];
            } else if (DifficultyLevel.Hard == Difficulty) {
                Map = new Map(Rand, 30, 30, 4);
                Ghosts = new Ghost[5];
            }

            for (var i = 0; i < Ghosts.Length; i++) {
                Ghosts[i] = new Ghost(Map);
            }

            Player = new Player(Map);
            State = StateEnum.Playing;
            Renderer = new GameRenderer(Window, this);
        }

        public override void Tick() {
            ++TicksCounter;
            var TicksPerSecond = (float)Window.TargetUpdateFrequency;
            var SecondsPlayed = TicksCounter / TicksPerSecond;

            if (StateEnum.Playing == State) {
                TorchLight = Math.Max(100 * (1 - SecondsPlayed / TorchLightLifeTime), 0);

                foreach (var Ghost in Ghosts) {
                    Ghost.Move(1 / TicksPerSecond);
                }

                if (Window.Keyboard[Key.Left]) {
                    Player.Angle -= Player.TurnSpeed / TicksPerSecond;
                }
                if (Window.Keyboard[Key.Right]) {
                    Player.Angle += Player.TurnSpeed / TicksPerSecond;
                }

                var PlayerMovementVector = Vector2.Zero;
                if (Window.Keyboard[Key.Up] || Window.Keyboard[Key.W]) {
                    PlayerMovementVector.Y += Player.MovementSpeed / TicksPerSecond;
                }
                if (Window.Keyboard[Key.Down] || Window.Keyboard[Key.S]) {
                    PlayerMovementVector.Y -= Player.MovementSpeed / TicksPerSecond;
                }
                if (Window.Keyboard[Key.A]) {
                    PlayerMovementVector.X -= Player.MovementSpeed / TicksPerSecond;
                }
                if (Window.Keyboard[Key.D]) {
                    PlayerMovementVector.X += Player.MovementSpeed / TicksPerSecond;
                }

                if (Vector2.Zero != PlayerMovementVector) {
                    Player.Move(PlayerMovementVector);
                }

                for (var i = 0; i < Map.Checkpoints.Count; i++) {
                    if (!CollectedCheckpoints.Contains(i) && ((Position)Player.Position == Map.Checkpoints[i])) {
                        CollectedCheckpoints.Add(i);
                    }
                }

                if (((Position)Player.Position == Map.FinishPosition) && (CollectedCheckpoints.Count == Map.Checkpoints.Count)) {
                    State = StateEnum.Win;
                }
            }

            Renderer.Tick();
        }

        public override void OnKeyPress(Key K) {
            if (StateEnum.Playing == State) {
                if (K.Equals(Key.C)) {
                    if (GameRenderer.CameraMode.FirstPerson == Renderer.Camera) {
                        Renderer.Camera = GameRenderer.CameraMode.ThirdPerson;

                    } else {
                        Renderer.Camera = GameRenderer.CameraMode.FirstPerson;
                    }
                } else if (K.Equals(Key.F)) {
                    if (MarksLeft > 0) {
                        Marks.Add((Position)Player.Position);
                        --MarksLeft;
                    }
                }
            }
        }

        public override void Render() {
            Renderer.Render();
        }
    }
}