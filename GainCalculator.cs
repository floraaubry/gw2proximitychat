using System;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Distance -> gain falloff and stereo pan.
    /// GW2's raw MumbleLink coordinates are X = right, Y = front/depth, Z = up -- NOT the
    /// "Y = up, Z = front" the project's original spec assumed. Confirmed via Blish HUD's
    /// own VectorUtil.UpVectorFromCameraForward source (Cross(camForward, Vector3.Backward)
    /// -- i.e. it derives camera-right using world Z as the up reference, so Z is up) after
    /// a range-indicator ring rendered visibly vertical/wall-like using the old X/Z ground
    /// plane assumption. The horizontal ground plane is therefore X/Y, not X/Z.
    /// </summary>
    public static class GainCalculator
    {
        // MumbleLink positions are in meters (confirmed via Blish HUD's own
        // WorldUtil.WorldToGameCoord doc comment: "Converts a world (meters) coordinate
        // to game (inches) coordinate" -- GW2's raw coordinates are the "world" side of
        // that, i.e. meters).
        //
        // Three-tier falloff: full volume out to NormalRange, a shallow degrade down to
        // DegradedGain out to DegradedRange, then a steeper degrade down to silence at
        // MaxRange.
        public const float NormalRange = 8f;
        public const float DegradedRange = 16f;
        public const float MaxRange = 60f;
        public const float DegradedGain = 0.85f;

        public static float DistanceToGain(float distance)
        {
            if (distance <= NormalRange) return 1f;
            if (distance <= DegradedRange)
            {
                float t = (distance - NormalRange) / (DegradedRange - NormalRange);
                return MathHelper.Lerp(1f, DegradedGain, t);
            }
            if (distance >= MaxRange) return 0f;

            float t2 = (distance - DegradedRange) / (MaxRange - DegradedRange);
            return MathHelper.Lerp(DegradedGain, 0f, t2);
        }

        public static float ComputePan(Vector3 listenerPosition, Vector3 listenerForward, Vector3 speakerPosition)
        {
            float dx = speakerPosition.X - listenerPosition.X;
            float dy = speakerPosition.Y - listenerPosition.Y;
            if (dx * dx + dy * dy < 0.0001f) return 0f;

            float fx = listenerForward.X;
            float fy = listenerForward.Y;
            float forwardLenSq = fx * fx + fy * fy;
            if (forwardLenSq < 0.0001f) return 0f;

            float right = fx * dy - fy * dx;
            float dot = fx * dx + fy * dy;
            float angle = (float)Math.Atan2(right, dot);

            return MathHelper.Clamp((float)Math.Sin(angle), -1f, 1f);
        }
    }
}
