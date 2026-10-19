using System.Collections.Generic;
using UnityEngine;

public class Flock
{
   public HashSet<Transform> members = new HashSet<Transform>();

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
   
   public float maxSpeed = 12.0f;
   public float maxForce = 5.0f;

   public float sightRadius = 8.0f;
   public float separationRadius = 3.0f;

   public float separationWeight = 1.6f;
   public float alignmentWeight = 1.2f;
   public float cohesionWeight = 0.15f;
   public float leaderAttractionWeight = 2.4f;
}