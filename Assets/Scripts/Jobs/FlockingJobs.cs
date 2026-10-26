using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct BuildGridJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<float3> Positions;

    public NativeParallelMultiHashMap<int3, int>.ParallelWriter Grid;

    public float CellSize;

    public void Execute(int index)
    {
        // SPATIAL HASH GRID
        int3 cell = (int3)math.floor(Positions[index] / CellSize);

        Grid.Add(cell, index);
    }
}

[BurstCompile]
public struct CalculateSteeringForceJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<float3> Positions;
    [ReadOnly]
    public NativeArray<float3> Velocities;
    
    [ReadOnly]
    public NativeArray<int> FlockIds;

    [ReadOnly]
    public NativeArray<FlockSettings> FlockSettingsById;

    [ReadOnly]
    public NativeParallelMultiHashMap<int3, int> Grid;
    
    [ReadOnly] 
    public NativeArray<NativeFlockLeader> Leaders;

    [WriteOnly]
    public NativeArray<float3> SteeringForces;

    public float CellSize;
    public int MaxNeighbors;

    public void Execute(int index)
    {
        int flockId = FlockIds[index];
        FlockSettings flock = FlockSettingsById[flockId];
        
        float3 position = Positions[index];
        float3 velocity = Velocities[index];
        int3 center = (int3)math.floor(position / CellSize);  // center of the cell

        float radiusSq = flock.SightRadius * flock.SightRadius;

        FixedList128Bytes<int> nearest = default;
        FixedList128Bytes<float> distances = default;

        // Assumes CellSize >= Radius [else more cells need to be searched]
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
                if (other == index)
                    continue;
                
                if (FlockIds[other] != flockId)
                    continue;

                // squared distance to other Agent
                float distSq = math.lengthsq(Positions[other] - position);
                
                if (distSq > radiusSq)  // perception radius check
                    continue;

                // Find insertion position by distance.
                int insert = 0;
                while (insert < distances.Length && distances[insert] <= distSq)
                    insert++;

                if (insert >= MaxNeighbors)
                    continue;

                if (nearest.Length < MaxNeighbors)
                {
                    nearest.Add(-1);
                    distances.Add(float.MaxValue);
                }

                // Make room for the new candidate.
                for (int j = nearest.Length - 1; j > insert; j--)
                {
                    nearest[j] = nearest[j - 1];
                    distances[j] = distances[j - 1];
                }

                nearest[insert] = other;
                distances[insert] = distSq;
            }
            while (Grid.TryGetNextValue(out other, ref iterator));
        }

        float3 separationForce = new float3(0.0f, 0.0f, 0.0f),
                alignmentForce = new float3(0.0f, 0.0f, 0.0f),
                cohesionForce = new float3(0.0f, 0.0f, 0.0f),
                leaderAttractionForce = new float3(0.0f, 0.0f, 0.0f);
        
        // SEPARATION
        float desiredSpacing = flock.SeparationRadius;
        float3 separationSum = new float3(0.0f, 0.0f, 0.0f);
        int separationCount = 0;

        int peerCount = 0;
        // ALIGNMENT
        float3 meanVelocity = new float3(0.0f, 0.0f, 0.0f);

        // COHESION
        float3 meanPosition = new float3(0.0f, 0.0f, 0.0f);
        
        for (int i = 0; i < math.min(flock.MaxPeers, nearest.Length); i++)
        {
            // normalized direction vector
            float3 otherPos = Positions[nearest[i]];
            float3 direction = otherPos - position;  // from us to peer
            float dist = math.length(direction);
            direction = math.normalizesafe(direction);
            
            // SEPARATION
            if (dist < desiredSpacing) {
                float3 sep = direction / math.max(0.01f, dist);  // the closer, the higher the repulsion
                separationSum += -1 * sep;
                separationCount++;
            }
            
            // ALIGNMENT
            meanVelocity += Velocities[nearest[i]];
            peerCount++;

            // COHESION
            meanPosition += otherPos;
        }
        
        // steer = desired - velocity

        // SEPARATION
        if (separationCount > 0)
        {
            separationSum = separationSum / separationCount;
            separationSum = math.normalizesafe(separationSum) * flock.MaxSpeed;

            separationForce = Limit(separationSum - velocity, flock.MaxForce);
        }

        if (peerCount > 0)
        {
            // ALIGNMENT
            meanVelocity = math.normalizesafe(meanVelocity / peerCount) * flock.MaxSpeed;
            // do not slow down, but try to hold maxSpeed while following the direction

            alignmentForce = Limit(meanVelocity - velocity, flock.MaxForce);

            // COHESION
            meanPosition /= peerCount;
            float3 desiredDir = math.normalizesafe(meanPosition - position) * flock.MaxSpeed;

            cohesionForce = Limit(desiredDir - velocity, flock.MaxForce);
        }

        // LEADER ATTRACTION
        NativeFlockLeader leader = Leaders[flock.LeaderId];

        float3 positionError = leader.Position - position;
        float3 velocityError = leader.Velocity - velocity;

        // Stronger position correction when falling behind.
        float3 catchUpAcceleration = positionError * flock.AttractionPositionWeight;

        // Match the leader's current velocity.
        float3 velocityMatchingAcceleration = velocityError * flock.AttractionVelocityWeight;

        leaderAttractionForce = Limit(catchUpAcceleration + velocityMatchingAcceleration, flock.MaxForce);
        
        
        
        float distanceToLeader = math.distance(position, leader.Position);

        // Stronger separation near the leader
        float separationMultiplier = 1f / math.max(distanceToLeader, 1f);

        float3 survivalSteering = Limit(
            separationForce * flock.SeparationWeight * separationMultiplier,
            flock.SeparationMaxAcceleration
        );

        float maxFollowingAcc = math.lengthsq(leader.Velocity) > 0.005f ? flock.FollowingMaxAccelerationInMotion : flock.FollowingMaxAcceleration;
        float3 followingSteering = Limit(
            leaderAttractionForce * flock.LeaderAttractionWeight
            + alignmentForce * flock.AlignmentWeight
            + cohesionForce * flock.CohesionWeight,
            maxFollowingAcc
        );

        SteeringForces[index] = survivalSteering + followingSteering;
    }
    
    private static float3 Limit(float3 vector, float maxLength)
    {
        float lengthSq = math.lengthsq(vector);
        float maxSq = maxLength * maxLength;

        if (lengthSq <= maxSq)
            return vector;

        return math.normalizesafe(vector) * maxLength;
    }
}