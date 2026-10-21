using System.Collections.Generic;
using UnityEngine;

public class Flock
{
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

   public float sightRadius = 8.0f;
   public float separationRadius = 6.0f;

   public float separationWeight = 0.5f;
   public float alignmentWeight = 1.2f;
   public float cohesionWeight = 0.15f;
   public float leaderAttractionWeight = 3f;
}

public struct FlockSettings
{
    public float MaxSpeed;
    public float MaxForce;

    public float SightRadius;
    public float SeparationRadius;

    public float SeparationWeight;
    public float AlignmentWeight;
    public float CohesionWeight;
    public float LeaderAttractionWeight;

    public int MinPeers;
    public int MaxPeers;
}