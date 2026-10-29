using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct CalculateHitUnitsJob : IJob
{
    [ReadOnly] public NativeArray<float3> Positions;
    //[ReadOnly] public NativeArray<float3> Velocities;
    
    //[ReadOnly] public NativeArray<int> FlockIds;

    [ReadOnly] public NativeParallelMultiHashMap<int3, int> Grid;

    [WriteOnly] public NativeReference<int> HitCount;

    public float CellSize;
    public float ProjectileRadius;
    public float DeadlyRadius;
    public float3 ProjectilePosition;
    public uint Seed;

    public void Execute()
    {
        int hitCount = 0;
        
        int3 center = (int3)math.floor(ProjectilePosition / CellSize);  // center of the cell [of the Projectile]

        float radiusSq = ProjectileRadius * ProjectileRadius;
        float deadlyRadiusSq = DeadlyRadius * DeadlyRadius;
        
        var random = new Unity.Mathematics.Random(math.max(Seed, 1u));

        // Search the containing cell and its 26 neighbors.
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            int3 cell = center + new int3(x, y, z);

            if (!Grid.TryGetFirstValue(cell, out int other, out var iterator))
                continue;

            do
            {
                // squared distance from Projectile to Agent
                float distSq = math.lengthsq(Positions[other] - ProjectilePosition);
                
                if (distSq > radiusSq)  // projectile affection radius check
                    continue;

                // distSq < DeadlyRadiusSq -> dead -> HitCount++
                // distSq == radiusSq  -> 0%
                // distSq == deadlyRadiusSq  -> 100%
                // radiusSq > deadlyRadiusSq

                float chanceToBeHit = math.saturate( (radiusSq - distSq) / (radiusSq - deadlyRadiusSq) );

                if (random.NextFloat() <= chanceToBeHit)  // [0, 1)
                    hitCount++;
            }
            while (Grid.TryGetNextValue(out other, ref iterator));
        }
        
        HitCount.Value = hitCount;
    }
}