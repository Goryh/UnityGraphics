using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompile(FloatMode = FloatMode.Default, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    struct TilingJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<SphericalLightData> lights;

        [NativeDisableParallelForRestriction]
        public NativeArray<InclusiveRange> tileRanges;

        public int lightCount;
        public int rangesPerLight;

        public Fixed2<float4x4> worldToViews;

        public float2 tileScale;
        public float2 tileScaleInv;
        public Fixed2<float> viewPlaneBottoms;
        public Fixed2<float> viewPlaneTops;
        public Fixed2<float4> viewToViewportScaleBiases;
        public int2 tileCount;
        public float near;
        public bool isOrthographic;

        InclusiveRange m_TileYRange;
        int m_Offset;
        int m_ViewIndex;

        public void Execute(int jobIndex)
        {
            var index = jobIndex % lightCount;
            m_ViewIndex = jobIndex / lightCount;
            m_Offset = jobIndex * rangesPerLight;

            m_TileYRange = new InclusiveRange(short.MaxValue, short.MinValue);

            for (var i = 0; i < rangesPerLight; i++)
            {
                tileRanges[m_Offset + i] = new InclusiveRange(short.MaxValue, short.MinValue);
            }

            if (isOrthographic) { TileLightOrthographic(index); }
            else { TileLight(index); }
        }

        void TileLight(int lightIndex)
        {
            var positionRange = lights[lightIndex].positionRange;
            var lightPositionVS = math.mul(worldToViews[m_ViewIndex], math.float4(positionRange.xyz, 1)).xyz;
            lightPositionVS.z *= -1;
            if (lightPositionVS.z >= near) ExpandY(lightPositionVS);

            var range = positionRange.w;
            var rangesq = square(range);

            // Radius of circle formed by intersection of sphere and near plane.
            // Found using Pythagoras with a right triangle formed by three points:
            // (a) light position
            // (b) light position projected to near plane
            // (c) a point on the near plane at a distance `range` from the light position
            //     (i.e. lies both on the sphere and the near plane)
            // Thus the hypotenuse is formed by (a) and (c) with length `range`, and the known side is formed
            // by (a) and (b) with length equal to the distance between the near plane and the light position.
            // The remaining unknown side is formed by (b) and (c) with length equal to the radius of the circle.
            var sphereClipRadius = math.sqrt(rangesq - square(near - lightPositionVS.z));

            // Project light sphere onto YZ plane, find the horizon points, and re-construct view space position of found points.
            GetSphereHorizon(lightPositionVS.yz, range, near, sphereClipRadius, out var sphereBoundYZ0, out var sphereBoundYZ1);
            ExpandY(math.float3(lightPositionVS.x, sphereBoundYZ0));
            ExpandY(math.float3(lightPositionVS.x, sphereBoundYZ1));

            // Project light sphere onto XZ plane, find the horizon points, and re-construct view space position of found points.
            GetSphereHorizon(lightPositionVS.xz, range, near, sphereClipRadius, out var sphereBoundXZ0, out var sphereBoundXZ1);
            ExpandY(math.float3(sphereBoundXZ0.x, lightPositionVS.y, sphereBoundXZ0.y));
            ExpandY(math.float3(sphereBoundXZ1.x, lightPositionVS.y, sphereBoundXZ1.y));

            m_TileYRange.Clamp(0, (short)(tileCount.y - 1));

            // Calculate tile plane ranges for sphere.
            for (var planeIndex = m_TileYRange.start + 1; planeIndex <= m_TileYRange.end; planeIndex++)
            {
                var planeRange = InclusiveRange.empty;

                var planeY = math.lerp(viewPlaneBottoms[m_ViewIndex], viewPlaneTops[m_ViewIndex], planeIndex * tileScaleInv.y);
                GetSphereYPlaneHorizon(lightPositionVS, range, near, sphereClipRadius, planeY, out var sphereTile0, out var sphereTile1);
                planeRange.Expand((short)math.clamp(ViewToTileSpace(sphereTile0).x, 0, tileCount.x - 1));
                planeRange.Expand((short)math.clamp(ViewToTileSpace(sphereTile1).x, 0, tileCount.x - 1));

                var tileIndex = m_Offset + 1 + planeIndex;
                tileRanges[tileIndex] = InclusiveRange.Merge(tileRanges[tileIndex], planeRange);
                tileRanges[tileIndex - 1] = InclusiveRange.Merge(tileRanges[tileIndex - 1], planeRange);
            }

            tileRanges[m_Offset] = m_TileYRange;
        }

        void TileLightOrthographic(int lightIndex)
        {
            var positionRange = lights[lightIndex].positionRange;
            var lightPosVS = math.mul(worldToViews[m_ViewIndex], math.float4(positionRange.xyz, 1)).xyz;
            lightPosVS.z *= -1;
            ExpandOrthographic(lightPosVS);

            var range = positionRange.w;
            var rangeSq = square(range);

            ExpandOrthographic(lightPosVS - math.float3(0, range, 0));
            ExpandOrthographic(lightPosVS + math.float3(0, range, 0));
            ExpandOrthographic(lightPosVS - math.float3(range, 0, 0));
            ExpandOrthographic(lightPosVS + math.float3(range, 0, 0));

            m_TileYRange.Clamp(0, (short)(tileCount.y - 1));

            // Tile plane ranges
            for (var planeIndex = m_TileYRange.start + 1; planeIndex <= m_TileYRange.end; planeIndex++)
            {
                var planeRange = InclusiveRange.empty;

                var planeY = math.lerp(viewPlaneBottoms[m_ViewIndex], viewPlaneTops[m_ViewIndex], planeIndex * tileScaleInv.y);
                var sphereX = math.sqrt(rangeSq - square(planeY - lightPosVS.y));
                ExpandRangeOrthographic(ref planeRange, lightPosVS.x - sphereX);
                ExpandRangeOrthographic(ref planeRange, lightPosVS.x + sphereX);

                var tileIndex = m_Offset + 1 + planeIndex;
                tileRanges[tileIndex] = InclusiveRange.Merge(tileRanges[tileIndex], planeRange);
                tileRanges[tileIndex - 1] = InclusiveRange.Merge(tileRanges[tileIndex - 1], planeRange);
            }

            tileRanges[m_Offset] = m_TileYRange;
        }

        /// <summary>
        /// Project onto Z=1, scale and offset into [0, tileCount]
        /// </summary>
        float2 ViewToTileSpace(float3 positionVS)
        {
            return (positionVS.xy / positionVS.z * viewToViewportScaleBiases[m_ViewIndex].xy + viewToViewportScaleBiases[m_ViewIndex].zw) * tileScale;
        }

        /// <summary>
        /// Project onto Z=1, scale and offset into [0, tileCount]
        /// </summary>
        float2 ViewToTileSpaceOrthographic(float3 positionVS)
        {
            return (positionVS.xy * viewToViewportScaleBiases[m_ViewIndex].xy + viewToViewportScaleBiases[m_ViewIndex].zw) * tileScale;
        }

        /// <summary>
        /// Expands the tile Y range and the X range in the row containing the position.
        /// </summary>
        void ExpandY(float3 positionVS)
        {
            // var positionTS = math.clamp(ViewToTileSpace(positionVS), 0, tileCount - 1);
            var positionTS = ViewToTileSpace(positionVS);
            var tileY = (int)positionTS.y;
            var tileX = (int)positionTS.x;
            m_TileYRange.Expand((short)math.clamp(tileY, 0, tileCount.y - 1));
            if (tileY >= 0 && tileY < tileCount.y && tileX >= 0 && tileX < tileCount.x)
            {
                var rowXRange = tileRanges[m_Offset + 1 + tileY];
                rowXRange.Expand((short)tileX);
                tileRanges[m_Offset + 1 + tileY] = rowXRange;
            }
        }

        /// <summary>
        /// Expands the tile Y range and the X range in the row containing the position.
        /// </summary>
        void ExpandOrthographic(float3 positionVS)
        {
            // var positionTS = math.clamp(ViewToTileSpace(positionVS), 0, tileCount - 1);
            var positionTS = ViewToTileSpaceOrthographic(positionVS);
            var tileY = (int)positionTS.y;
            var tileX = (int)positionTS.x;
            m_TileYRange.Expand((short)math.clamp(tileY, 0, tileCount.y - 1));
            if (tileY >= 0 && tileY < tileCount.y && tileX >= 0 && tileX < tileCount.x)
            {
                var rowXRange = tileRanges[m_Offset + 1 + tileY];
                rowXRange.Expand((short)tileX);
                tileRanges[m_Offset + 1 + tileY] = rowXRange;
            }
        }

        void ExpandRangeOrthographic(ref InclusiveRange range, float xVS)
        {
            range.Expand((short)math.clamp(ViewToTileSpaceOrthographic(xVS).x, 0, tileCount.x - 1));
        }

        static float square(float x) => x * x;

        /// <summary>
        /// Finds the two horizon points seen from (0, 0) of a sphere projected onto either XZ or YZ. Takes clipping into account.
        /// </summary>
        static void GetSphereHorizon(float2 center, float radius, float near, float clipRadius, out float2 p0, out float2 p1)
        {
            var direction = math.normalize(center);

            // Distance from camera to center of sphere
            var d = math.length(center);

            // Distance from camera to sphere horizon edge
            var l = math.sqrt(d * d - radius * radius);

            // Height of circle horizon
            var h = l * radius / d;

            // Center of circle horizon
            var c = direction * (l * h / radius);

            p0 = math.float2(float.MinValue, 1f);
            p1 = math.float2(float.MaxValue, 1f);

            // Handle clipping
            if (center.y - radius < near)
            {
                p0 = math.float2(center.x + clipRadius, near);
                p1 = math.float2(center.x - clipRadius, near);
            }

            // Circle horizon points
            var c0 = c + math.float2(-direction.y, direction.x) * h;
            if (square(d) >= square(radius) && c0.y >= near)
            {
                if (c0.x > p0.x) { p0 = c0; }
                if (c0.x < p1.x) { p1 = c0; }
            }

            var c1 = c + math.float2(direction.y, -direction.x) * h;
            if (square(d) >= square(radius) && c1.y >= near)
            {
                if (c1.x > p0.x) { p0 = c1; }
                if (c1.x < p1.x) { p1 = c1; }
            }
        }

        static void GetSphereYPlaneHorizon(float3 center, float sphereRadius, float near, float clipRadius, float y, out float3 left, out float3 right)
        {
            // Note: The y-plane is the plane that is determined by `y` in that it contains the vector (1, 0, 0)
            // and goes through the points (0, y, 1) and (0, 0, 0). This would become a straight line in screen-space, and so it
            // represents the boundary between two rows of tiles.

            // Near-plane clipping - will get overwritten if no clipping is needed.
            // `y` is given for the view plane (Z=1), scale it so that it is on the near plane instead.
            var yNear = y * near;
            // Find the two points of intersection between the clip circle of the sphere and the y-plane.
            // Found using Pythagoras with a right triangle formed by three points:
            // (a) center of the clip circle
            // (b) a point straight above the clip circle center on the y-plane
            // (c) a point that is both on the circle and the y-plane (this is the point we want to find in the end)
            // The hypotenuse is formed by (a) and (c) with length equal to the clip radius. The known side is
            // formed by (a) and (b) and is simply the distance from the center to the y-plane along the y-axis.
            // The remaining side gives us the x-displacement needed to find the intersection points.
            var clipHalfWidth = math.sqrt(square(clipRadius) - square(yNear - center.y));
            left = math.float3(center.x - clipHalfWidth, yNear, near);
            right = math.float3(center.x + clipHalfWidth, yNear, near);

            // Basis vectors in the y-plane for being able to parameterize the plane.
            var planeU = math.normalize(math.float3(0, y, 1));
            var planeV = math.float3(1, 0, 0);

            // Calculate the normal of the y-plane. Found from: (0, y, 1) × (1, 0, 0) = (0, 1, -y)
            // This is used to represent the plane along with the origin, which is just 0 and thus doesn't show up
            // in the calculations.
            var normal = math.normalize(math.float3(0, 1, -y));

            // We want to first find the circle from the intersection of the y-plane and the sphere.

            // The shortest distance from the sphere center and the y-plane. The sign determines which side of the plane
            // the center is on.
            var signedDistance = math.dot(normal, center);

            // Unsigned shortest distance from the sphere center to the plane.
            var distanceToPlane = math.abs(signedDistance);

            // The center of the intersection circle in the y-plane, which is the point on the plane closest to the
            // sphere center. I.e. this is at `distanceToPlane` from the center.
            var centerOnPlane = math.float2(math.dot(center, planeU), math.dot(center, planeV));

            // Distance from origin to the circle center.
            var distanceInPlane = math.length(centerOnPlane);

            // Direction from origin to the circle center.
            var directionPS = centerOnPlane / distanceInPlane;

            // Calculate the radius of the circle using Pythagoras. We know that any point on the circle is a point on
            // the sphere. Thus we can construct a triangle with the sphere center, circle center, and a point on the
            // circle. We then want to find its distance to the circle center, as that will be equal to the radius. As
            // the point is on the sphere, it must be `sphereRadius` from the sphere center, forming the hypotenuse. The
            // other side is between the sphere and circle centers, which we've already calculated to be
            // `distanceToPlane`.
            var circleRadius = math.sqrt(square(sphereRadius) - square(distanceToPlane));

            // Now that we have the circle, we can find the horizon points. Since we've parametrized the plane, we can
            // just do this in 2D.

            // Any of these conditions will yield NaN due to negative square roots. They are signs that clipping is needed,
            // so we fallback on the already calculated values in that case.
            if (square(distanceToPlane) <= square(sphereRadius) && square(circleRadius) <= square(distanceInPlane))
            {
                // Distance from origin to circle horizon edge.
                var l = math.sqrt(square(distanceInPlane) - square(circleRadius));

                // Height of circle horizon.
                var h = l * circleRadius / distanceInPlane;

                // Center of circle horizon.
                var c = directionPS * (l * h / circleRadius);

                // Calculate the horizon points in the plane.
                var leftOnPlane = c + math.float2(directionPS.y, -directionPS.x) * h;
                var rightOnPlane = c + math.float2(-directionPS.y, directionPS.x) * h;

                // Transform horizon points to view space and use if not clipped.
                var leftCandidate = leftOnPlane.x * planeU + leftOnPlane.y * planeV;
                if (leftCandidate.z >= near) left = leftCandidate;

                var rightCandidate = rightOnPlane.x * planeU + rightOnPlane.y * planeV;
                if (rightCandidate.z >= near) right = rightCandidate;
            }
        }
    }
}
