using System;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Distance -> gain falloff and stereo pan, per the project's proximity-audio spec.
    /// MumbleLink follows Mumble's official positional-audio convention: X = right,
    /// Y = up, Z = front. The horizontal ground plane is therefore X/Z, not X/Y.
    /// </summary>
    public static class GainCalculator
    {
        public const float MinRange = 100f;
        public const float MaxRange = 1500f;

        public static float DistanceToGain(float distance)
        {
            if (distance <= MinRange) return 1f;
            if (distance >= MaxRange) return 0f;

            float linear = 1f - (distance - MinRange) / (MaxRange - MinRange);
            return linear * linear;
        }

        public static float ComputePan(Vector3 listenerPosition, Vector3 listenerForward, Vector3 speakerPosition)
        {
            float dx = speakerPosition.X - listenerPosition.X;
            float dz = speakerPosition.Z - listenerPosition.Z;
            if (dx * dx + dz * dz < 0.0001f) return 0f;

            float fx = listenerForward.X;
            float fz = listenerForward.Z;
            float forwardLenSq = fx * fx + fz * fz;
            if (forwardLenSq < 0.0001f) return 0f;

            float right = fx * dz - fz * dx;
            float dot = fx * dx + fz * dz;
            float angle = (float)Math.Atan2(right, dot);

            return MathHelper.Clamp((float)Math.Sin(angle), -1f, 1f);
        }
    }
}
