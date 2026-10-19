// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Data;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using System;
using MathHelper = Microsoft.Xna.Framework.MathHelper;

namespace ClassicUO.Game
{
    enum WeatherType
    {
        WT_RAIN = 0,
        WT_STORM_APPROACH,
        WT_SNOW,
        WT_STORM_BREWING,

        WT_INVALID_0 = 0xFE,
        WT_INVALID_1 = 0xFF
    }

    internal sealed class Weather
    {
        private const int MAX_WEATHER_EFFECT = 250;
        private const int MAX_WEATHER_PARTICLES = 2000;
        private const float SIMULATION_TIME = 37.0f;
        private const float RISE_TIME = 10000.0f; // ms from no particles to the full count

        private readonly WeatherEffect[] _effects = new WeatherEffect[MAX_WEATHER_PARTICLES];
        private uint _timer, _windTimer, _lastTick;
        private float _pendingParticles;
        private readonly World _world;

        public Weather(World world)
        {
            _world = world;
        }


        public WeatherType? CurrentWeather { get; private set; }
        public WeatherType Type { get; private set; }
        public byte Count { get; private set; }
        public int CurrentCount { get; private set; }
        public byte Temperature{ get; private set; }
        public sbyte Wind { get; private set; }



        private static float SinOscillate(float freq, int range, uint current_tick)
        {
            float anglef = (int) (current_tick / 2.7777f * freq) % 360;

            return (float) Math.Sin(MathHelper.ToRadians(anglef)) * range;
        }


        public void Reset()
        {
            Type = 0;
            Count = Temperature = 0;
            CurrentCount = 0;
            Wind = 0;
            _windTimer = _timer = 0;
            _pendingParticles = 0;
            CurrentWeather = null;
        }

        // A packet only sets the type and the density that Draw works toward. Particles of the type are added
        // over RISE_TIME, and a particle that is no longer wanted is removed when it leaves the view. So a start,
        // a change of density, an end and a change of type are all gradual, and two types mix during a change.
        public void Generate(WeatherType type, byte count, byte temp)
        {
            Temperature = temp;

            if (type == WeatherType.WT_INVALID_0 || type == WeatherType.WT_INVALID_1 || count == 0)
            {
                Count = 0;
                CurrentWeather = null;

                return;
            }

            // ServUO resends the weather every 30 seconds, so a repeat of the current type shows no new message
            bool isNewType = CurrentWeather != type;

            Type = type;
            Count = (byte) Math.Min(MAX_WEATHER_EFFECT, (int) count);
            _timer = Time.Ticks + Constants.WEATHER_TIMER;
            CurrentWeather = type;

            if (!isNewType)
            {
                return;
            }

            _windTimer = 0;

            switch (type)
            {
                case WeatherType.WT_RAIN:
                    GameActions.Print
                    (
                        _world,
                        ResGeneral.ItBeginsToRain,
                        1154,
                        MessageType.System,
                        3,
                        false
                    );

                    break;

                case WeatherType.WT_STORM_APPROACH:
                    GameActions.Print
                    (
                        _world,
                        ResGeneral.AFierceStormApproaches,
                        1154,
                        MessageType.System,
                        3,
                        false
                    );

                    PlayThunder();

                    break;

                case WeatherType.WT_SNOW:
                    GameActions.Print
                    (
                        _world,
                        ResGeneral.ItBeginsToSnow,
                        1154,
                        MessageType.System,
                        3,
                        false
                    );

                    PlayWind();

                    break;

                case WeatherType.WT_STORM_BREWING:
                    GameActions.Print
                    (
                        _world,
                        ResGeneral.AStormIsBrewing,
                        1154,
                        MessageType.System,
                        3,
                        false
                    );

                    PlayThunder();

                    break;
            }
        }

        private static int CalculateScaledCount(byte count)
        {
            if (count <= 0)
            {
                return 0;
            }
            float legacyWindowSize = 640 * 480;
            return (int)Math.Max(1, Math.Min(MAX_WEATHER_PARTICLES, count * (Client.Game.Scene.Camera.Bounds.Width * Client.Game.Scene.Camera.Bounds.Height) / legacyWindowSize));
        }

        private void PlayWind()
        {
            PlaySound(RandomHelper.RandomList(0x014, 0x015, 0x016));
        }

        private void PlayThunder()
        {
           PlaySound(RandomHelper.RandomList(0x028, 0x206));
        }

        private void PlaySound(int sound)
        {
            // randomize the sound of the weather around the player
            int randX = RandomHelper.GetValue(10, 18);
            if (RandomHelper.RandomBool())
            {
                randX *= -1;
            }

            int randY = RandomHelper.GetValue(10, 18);
            if (RandomHelper.RandomBool())
            {
                randY *= -1;
            }

            Client.Game.Audio.PlaySoundWithDistance(_world, sound, _world.Player.X + randX, _world.Player.Y + randY);
        }

        private void AddParticle(WeatherType type, Point winsize)
        {
            ref WeatherEffect effect = ref _effects[CurrentCount++];

            effect.Type = type;
            effect.X = RandomHelper.GetValue(0, winsize.X);
            effect.Y = RandomHelper.GetValue(0, winsize.Y);

            // OrionUO (WeatherEffeect.cpp): the ID shifts the sway phase, and the scale ratio adds up to about 1.5 to the speed
            effect.ID = (uint) RandomHelper.GetValue(0, 1999);
            effect.ScaleRatio = (effect.ID % 20) / 13.0f;
        }

        public void Draw(UltimaBatcher2D batcher, int x, int y, float layerDepth)
        {
            int targetCount = _timer < Time.Ticks ? 0 : CalculateScaledCount(Count);

            if (CurrentCount == 0 && targetCount == 0)
            {
                // The weather has ended and its last particle has left the view
                Reset();
                _lastTick = Time.Ticks;

                return;
            }

            uint passed = Time.Ticks - _lastTick;

            if (passed > 7000)
            {
                _lastTick = Time.Ticks;
                passed = 25;
            }

            bool windChanged = false;

            if (_windTimer < Time.Ticks)
            {
                if (_windTimer == 0)
                {
                    windChanged = true;
                }

                _windTimer = Time.Ticks + (uint) (RandomHelper.GetValue(13, 19) * 1000);

                sbyte lastWind = Wind;

                Wind = (sbyte) RandomHelper.GetValue(0, 4);

                if (RandomHelper.GetValue(0, 1) != 0)
                {
                    Wind *= -1;
                }

                if (Wind < 0 && lastWind > 0)
                {
                    Wind = 0;
                }
                else if (Wind > 0 && lastWind < 0)
                {
                    Wind = 0;
                }

                if (lastWind != Wind)
                {
                    windChanged = true;
                }
            }

            if (windChanged && targetCount > 0)
            {
                switch (Type)
                {
                    case WeatherType.WT_STORM_APPROACH:
                    case WeatherType.WT_STORM_BREWING:
                        PlayThunder();

                        break;

                    case WeatherType.WT_SNOW:
                        PlayWind();

                        break;
                }
            }

            //switch ((WEATHER_TYPE) Type)
            //{
            //    case WEATHER_TYPE.WT_RAIN:
            //    case WEATHER_TYPE.WT_FIERCE_STORM:
            //        // TODO: set color
            //        break;
            //    case WEATHER_TYPE.WT_SNOW:
            //    case WEATHER_TYPE.WT_STORM:
            //        // TODO: set color
            //        break;
            //    default:
            //        break;
            //}

            //Point winpos = ProfileManager.CurrentProfile.GameWindowPosition;
            Point winsize = new Point(Client.Game.Scene.Camera.Bounds.Width, Client.Game.Scene.Camera.Bounds.Height);

            // Add particles of the current type until the target count is reached
            int typeCount = 0;

            for (int i = 0; i < CurrentCount; i++)
            {
                if (_effects[i].Type == Type)
                {
                    typeCount++;
                }
            }

            if (typeCount < targetCount)
            {
                _pendingParticles += targetCount * passed / RISE_TIME;

                int added = Math.Min((int) _pendingParticles, Math.Min(targetCount - typeCount, MAX_WEATHER_PARTICLES - CurrentCount));

                for (int i = 0; i < added; i++)
                {
                    AddParticle(Type, winsize);
                }

                typeCount += added;
                _pendingParticles -= added;
            }
            else
            {
                _pendingParticles = 0;
            }

            Rectangle snowRect = new Rectangle(0, 0, 2, 2);

            for (int i = 0; i < CurrentCount; i++)
            {
                ref WeatherEffect effect = ref _effects[i];

                if (effect.X < x || effect.X > x + winsize.X || effect.Y < y || effect.Y > y + winsize.Y)
                {
                    // Remove a particle of another type, or one above the target count, and fill its slot with the last one
                    if (effect.Type != Type || typeCount > targetCount)
                    {
                        if (effect.Type == Type)
                        {
                            typeCount--;
                        }

                        effect = _effects[--CurrentCount];
                        i--;

                        continue;
                    }

                    effect.X = x + RandomHelper.GetValue(0, winsize.X);
                    effect.Y = y + RandomHelper.GetValue(0, winsize.Y);
                }


                switch (effect.Type)
                {
                    case WeatherType.WT_RAIN:
                        float scaleRation = effect.ScaleRatio;
                        effect.SpeedX = -4.5f - scaleRation;
                        effect.SpeedY = 5.0f + scaleRation;

                        break;

                    case WeatherType.WT_STORM_APPROACH:
                        effect.SpeedX = Wind;
                        effect.SpeedY = 6.0f;

                        break;

                    case WeatherType.WT_SNOW:
                    case WeatherType.WT_STORM_BREWING:

                        if (effect.Type == WeatherType.WT_SNOW)
                        {
                            effect.SpeedX = Wind;
                            effect.SpeedY = 1.0f;
                        }
                        else
                        {
                            effect.SpeedX = Wind * 1.5f;
                            effect.SpeedY = 1.5f;
                        }

                        // Set on every frame, so a particle that was just added moves at once
                        effect.SpeedAngle = MathHelper.ToDegrees((float) Math.Atan2(effect.SpeedX, effect.SpeedY));

                        effect.SpeedMagnitude = (float) Math.Sqrt(Math.Pow(effect.SpeedX, 2) + Math.Pow(effect.SpeedY, 2));

                        float speedAngle = effect.SpeedAngle;
                        float speedMagnitude = effect.SpeedMagnitude;

                        speedMagnitude += effect.ScaleRatio;

                        speedAngle += SinOscillate(0.4f, 20, Time.Ticks + effect.ID);

                        float rad = MathHelper.ToRadians(speedAngle);
                        effect.SpeedX = speedMagnitude * (float) Math.Sin(rad);
                        effect.SpeedY = speedMagnitude * (float) Math.Cos(rad);

                        break;
                }

                float speedOffset = passed / SIMULATION_TIME;

                switch (effect.Type)
                {
                    case WeatherType.WT_RAIN:
                    case WeatherType.WT_STORM_APPROACH:

                        int oldX = (int) effect.X;
                        int oldY = (int) effect.Y;

                        float ofsx = effect.SpeedX * speedOffset;
                        float ofsy = effect.SpeedY * speedOffset;

                        effect.X += ofsx;
                        effect.Y += ofsy;

                        const float MAX_OFFSET_XY = 5.0f;

                        if (ofsx >= MAX_OFFSET_XY)
                        {
                            oldX = (int) (effect.X - MAX_OFFSET_XY);
                        }
                        else if (ofsx <= -MAX_OFFSET_XY)
                        {
                            oldX = (int) (effect.X + MAX_OFFSET_XY);
                        }

                        if (ofsy >= MAX_OFFSET_XY)
                        {
                            oldY = (int) (effect.Y - MAX_OFFSET_XY);
                        }
                        else if (oldY <= -MAX_OFFSET_XY)
                        {
                            oldY = (int) (effect.Y + MAX_OFFSET_XY);
                        }

                        Vector2 start = new Vector2(x + oldX, y + oldY);
                        Vector2 end = new Vector2(x + effect.X, y + effect.Y);

                        batcher.DrawLine
                        (
                           SolidColorTextureCache.GetTexture(Color.Blue),
                           start,
                           end,
                           Vector3.UnitZ,
                           2,
                           layerDepth
                        );

                        break;

                    case WeatherType.WT_SNOW:
                    case WeatherType.WT_STORM_BREWING:

                        effect.X += effect.SpeedX * speedOffset;
                        effect.Y += effect.SpeedY * speedOffset;

                        snowRect.X = x + (int) effect.X;
                        snowRect.Y = y + (int) effect.Y;

                        batcher.Draw
                        (
                            SolidColorTextureCache.GetTexture(Color.White),
                            snowRect,
                            Vector3.UnitZ,
                            layerDepth
                        );

                        break;
                }
            }

            _lastTick = Time.Ticks;
        }


        private struct WeatherEffect
        {
            public float SpeedX, SpeedY, X, Y, ScaleRatio, SpeedAngle, SpeedMagnitude;
            public uint ID;
            public WeatherType Type;
        }
    }
}
