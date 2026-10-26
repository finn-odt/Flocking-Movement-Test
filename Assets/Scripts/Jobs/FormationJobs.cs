using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public struct SurfaceAttachment
{
    public int3 Triangle;
    public float3 Barycentric;
}

/// <summary>
/// One-time job. Chooses triangles proportionally to local surface area,
/// then samples one barycentric point per agent in mesh-local coordinates.
/// </summary>
[BurstCompile]
public struct SampleFormationPointsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Vertices;
    [ReadOnly] public NativeArray<int3> Triangles;
    [ReadOnly] public NativeArray<float> CumulativeAreas;

    [WriteOnly] public NativeArray<float3> LocalTargets;  // output
    [WriteOnly] public NativeArray<SurfaceAttachment> Attachments;  // output

    public float TotalArea;
    public uint Seed;

    public void Execute(int index)
    {
        uint state = math.hash(new uint2((uint)index, Seed));
        var random = new Unity.Mathematics.Random(math.max(state, 1u));

        // Binary-search triangle by area. All triangles have positive area.
        float areaSample = random.NextFloat() * TotalArea;
        int lo = 0;
        int hi = CumulativeAreas.Length - 1;

        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (areaSample < CumulativeAreas[mid])
                hi = mid;
            else
                lo = mid + 1;
        }

        int3 tri = Triangles[lo];
        float3 a = Vertices[tri.x];
        float3 b = Vertices[tri.y];
        float3 c = Vertices[tri.z];

        // Uniform sampling on a triangle, not uniform barycentric weights.
        float u = random.NextFloat();
        float v = random.NextFloat();
        float su = math.sqrt(u);
        
        float3 weights = new float3(
            1f - su,
            su * (1f - v),
            su * v
        );

        Attachments[index] = new SurfaceAttachment
        {
            Triangle = tri,
            Barycentric = weights
        };

        LocalTargets[index] = weights.x * a + weights.y * b + weights.z * c;
    }
}

/// <summary>
/// Run every FixedUpdate while formation mode is enabled.
/// A PD controller drives each Rigidbody toward its own moving surface target.
/// Both current and previous matrices include the target's parent movement,
/// rotation and scale; their difference estimates target-point velocity.
/// </summary>
[BurstCompile]
public struct CalculateFormationForceJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    [ReadOnly] public NativeArray<float3> Velocities;
    [ReadOnly] public NativeArray<float3> LocalTargets;
    [WriteOnly] public NativeArray<float3> SteeringForces;  // output

    public float4x4 CurrentLocalToWorld;
    public float4x4 PreviousLocalToWorld;
    public float FixedDeltaTime;
    public float Stiffness;
    public float Damping;
    public float MaxAcceleration;

    public void Execute(int index)
    {
        float3 targetLocal = LocalTargets[index];
        // transform target to world coordinates
        float3 target = math.transform(CurrentLocalToWorld, targetLocal);
        float3 oldTarget = math.transform(PreviousLocalToWorld, targetLocal);

        float3 targetVelocity = (target - oldTarget) / math.max(FixedDeltaTime, 1e-5f);  // movement difference over time (of Target)
        float3 positionError = target - Positions[index];  // distance from Agent to Target
        float3 velocityError = targetVelocity - Velocities[index];  // difference between Agent- and Target-Velocity

        float3 acceleration = Stiffness * positionError + Damping * velocityError;
        float maxSq = MaxAcceleration * MaxAcceleration;
        if (math.lengthsq(acceleration) > maxSq)
            acceleration = math.normalizesafe(acceleration) * MaxAcceleration;

        SteeringForces[index] = acceleration;
    }
}
