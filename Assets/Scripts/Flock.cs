using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class Flock
{
    public int leaderId;
    
    public float minSpeed;

    public int minPeersInSight = 3;
    public int maxPeersInSight = 7;

    /*
    public float maxSpeed = 20.0f;
    public float maxForce = 5.0f;

    public float sightRadius = 10.0f;
    public float separationRadius = 1.0f;
   
    public float separationWeight = 0.9f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 0.4f;
    public float leaderAttractionWeight = 4.0f;
    */
   
    public float maxSpeed = 40.0f;
    public float maxForce = 8.0f;
    public float separationMaxAcceleration = 35f;
    public float followingMaxAcceleration = 5f;
    public float followingMaxAccelerationInMotion = 20f;

    public float attractionPositionWeight = 4.5f;
    public float attractionVelocityWeight = 6f;

    public float sightRadius = 20.0f;
    public float separationRadius = 12.0f;

    public float separationWeight = 0.7f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 0.1f;
    public float leaderAttractionWeight = 3f;
}

public struct FlockSettings
{
    public int LeaderId;
    
    public float MaxSpeed;
    public float MaxForce;
    public float SeparationMaxAcceleration;
    public float FollowingMaxAcceleration;
    public float FollowingMaxAccelerationInMotion;
    
    public float AttractionPositionWeight;
    public float AttractionVelocityWeight;

    public float SightRadius;
    public float SeparationRadius;

    public float SeparationWeight;
    public float AlignmentWeight;
    public float CohesionWeight;
    public float LeaderAttractionWeight;

    public int MinPeers;
    public int MaxPeers;
}

public struct NativeFlockLeader
{
    public float3 Position;
    public float3 Velocity;
    public float MaxSpeed;
}

[Serializable]
public struct FlockLeader
{
    public FlockLeader(Transform t, Rigidbody r, float maxSpeed)
    {
        transform = t;
        rigidbody = r;
        this.maxSpeed = maxSpeed;
    }
    public Transform transform;
    public Rigidbody rigidbody;
    public float maxSpeed;
}