using System;
using Blish_HUD;
using Blish_HUD.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GW2ProximityChat
{
    /// <summary>
    /// Draws three ground-plane rings around the player at GainCalculator's NormalRange
    /// (full volume), DegradedRange (85% volume), and MaxRange (silence) so the proximity
    /// falloff can be checked visually in-game. Registered once with
    /// <see cref="GameService.Graphics"/>'s <c>World</c> (Blish_HUD.Entities.IWorld) for the
    /// module's lifetime; actual visibility is gated on
    /// <see cref="ProximityService.ShowRangeIndicator"/> so it's a no-op render when the
    /// Debug tab's checkbox is off.
    /// </summary>
    public class RangeIndicatorEntity : IEntity
    {
        private const int Segments = 64;

        private static readonly Color NormalRangeColor = Color.LightGreen;
        private static readonly Color DegradedRangeColor = Color.Yellow;
        private static readonly Color MaxRangeColor = Color.OrangeRed;

        private readonly ProximityService _proximityService;
        private readonly VertexPositionColor[] _normalRingVertices = new VertexPositionColor[Segments + 1];
        private readonly VertexPositionColor[] _degradedRingVertices = new VertexPositionColor[Segments + 1];
        private readonly VertexPositionColor[] _maxRingVertices = new VertexPositionColor[Segments + 1];

        // Created lazily from the GraphicsDevice handed to Render -- avoids needing
        // GameService.Graphics.LendGraphicsDeviceContext() (GraphicsService.GraphicsDevice
        // itself is obsolete/errors, per TextureHelper's note) since Render already runs on
        // the correct graphics thread with a live device.
        private BasicEffect _effect;

        public float DrawOrder => 0f;

        public RangeIndicatorEntity(ProximityService proximityService)
        {
            _proximityService = proximityService;
        }

        public void Update(GameTime gameTime) { }

        public void Render(GraphicsDevice graphicsDevice, IWorld world, ICamera camera)
        {
            var mumble = GameService.Gw2Mumble;
            if (!_proximityService.ShowRangeIndicator || !mumble.IsAvailable) return;

            _effect = _effect ?? new BasicEffect(graphicsDevice) { VertexColorEnabled = true, World = Matrix.Identity };

            var playerCamera = mumble.PlayerCamera;
            _effect.View = playerCamera.View;
            _effect.Projection = playerCamera.Projection;

            var center = mumble.PlayerCharacter.Position;
            FillRing(_normalRingVertices, center, GainCalculator.NormalRange, NormalRangeColor);
            FillRing(_degradedRingVertices, center, GainCalculator.DegradedRange, DegradedRangeColor);
            FillRing(_maxRingVertices, center, GainCalculator.MaxRange, MaxRangeColor);

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineStrip, _normalRingVertices, 0, Segments);
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineStrip, _degradedRingVertices, 0, Segments);
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineStrip, _maxRingVertices, 0, Segments);
            }
        }

        // Ground plane is X/Y (Z = up), same convention GainCalculator.ComputePan uses.
        private static void FillRing(VertexPositionColor[] vertices, Vector3 center, float radius, Color color)
        {
            for (int i = 0; i <= Segments; i++)
            {
                float angle = MathHelper.TwoPi * i / Segments;
                var point = center + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0f);
                vertices[i] = new VertexPositionColor(point, color);
            }
        }
    }
}
