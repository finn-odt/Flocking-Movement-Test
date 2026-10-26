using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public interface ILeaderAgent
{
    LeaderManager Manager { get; set; }
}

[RequireComponent(typeof(Rigidbody))]
public class LeaderAgent : MonoBehaviour, ILeaderAgent
{
    public LeaderManager Manager { get; set; }
    
    private Rigidbody rb;
    
    private float pathProgress;
    private bool pathFinished;
    private bool wasDrawing;
    
    private int currentSegment;
    
    [SerializeField] private float moveSpeed = 15f;
    [SerializeField] private float maxAcceleration = 30f;
    [SerializeField] private float arrivalRadius = 0.05f;
    [SerializeField] private float slowingRadius = 3f;
    [SerializeField] private float lookAheadDistance = 2f;
    [SerializeField] private float responseTime = 0.1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        // if(manager == null) is expensive...
        
        var path = Manager.SlashLocations;
        
        // Detect the beginning of a new slash
        if (Manager.DrawingActive && !wasDrawing)
        {
            pathProgress = 0f;
            pathFinished = false;
            currentSegment = 0;
        }
        wasDrawing = Manager.DrawingActive;
        
        if (path.Count < 2 || pathFinished)
            return;
        

        Vector3 pos = rb.position;
        // Find closest point on the path, without going backward relative to previous progress
        pathProgress = Mathf.Max(
            pathProgress,
            FindClosestProgress(path, Manager.SlashDistances, rb.position)
        );

        var distances = Manager.SlashDistances;

        // update progress and segment index
        while (currentSegment + 1 < distances.Count - 1 && distances[currentSegment + 1] <= pathProgress)
            currentSegment++;
        
        float totalLength = Manager.SlashLength;
        
        // Aim farther along the path
        float targetProgress = Mathf.Min(pathProgress + lookAheadDistance, totalLength);

        Vector3 target = EvaluatePath(path, Manager.SlashDistances, targetProgress, currentSegment);

        Vector3 offset = target - pos;
        float distance = offset.magnitude;

        // Only use arrival behavior at the completed end
        bool finalTarget = !Manager.DrawingActive && targetProgress >= totalLength - 0.001f;

        if (finalTarget && distance <= arrivalRadius && rb.linearVelocity.magnitude < 0.5f)
        {
            pathFinished = true;
            rb.linearVelocity = Vector3.zero;  // stop immediately
            return;
        }

        // slow down when reaching the final target
        float desiredSpeed = moveSpeed;
        if (finalTarget)
            desiredSpeed *= Mathf.Clamp01( distance / slowingRadius);

        Vector3 desiredVelocity = distance > 0.0001f ? offset / distance * desiredSpeed : Vector3.zero;

        // Velocity tracking with acceleration limiting [steering = desired - velocity]
        Vector3 acceleration = (desiredVelocity - rb.linearVelocity) / Mathf.Max(responseTime, 0.01f);
        acceleration = Vector3.ClampMagnitude(acceleration, maxAcceleration);

        rb.AddForce(acceleration, ForceMode.Acceleration);
    }

    private float FindClosestProgress(IReadOnlyList<Vector3> path, IReadOnlyList<float> distances, Vector3 position)
    {
        float bestDistanceSq = float.MaxValue;
        float bestProgress = pathProgress;  // cached progress so far
        
        // Only look a few segments forward
        int end = Mathf.Min(currentSegment + 8, path.Count - 2);
        int start = Mathf.Max(1, currentSegment);

        for (int i = start; i <= end; i++)
        {
            // build segments of the path (point to point)
            Vector3 a = path[i - 1];
            Vector3 b = path[i];

            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;  // segment length

            if (lengthSq < 1e-8f)
                continue;
            
            // full length of the path to the end of this segment
            float length = distances[i + 1] - distances[i];
            
            // Restrict projection to currentSegment or later progress.
            float minT = (i == currentSegment) ? Mathf.Clamp01( (pathProgress - distances[i]) / length) : 0f;

            // project leader-position onto segment AB (to track progress along the line)
            float t = Mathf.Clamp(Vector3.Dot(position - a, ab) / lengthSq, minT, 1f);

            Vector3 closest = a + ab * t;
            float distSq = (position - closest).sqrMagnitude;

            if (distSq < bestDistanceSq)
            {
                bestDistanceSq = distSq;
                bestProgress = distances[i] + t * length;
            }
        }

        return bestProgress;
    }

    private static Vector3 EvaluatePath(IReadOnlyList<Vector3> path, IReadOnlyList<float> distances, float progress, int startSegment)
    {
        if (path.Count < 2 || distances.Count != path.Count)
            return path.Count > 0 ? path[^1] : Vector3.zero;

        int i = Mathf.Clamp(startSegment, 0, path.Count - 2);

        while (i < path.Count - 2 && distances[i + 1] < progress)
            i++;

        float segmentLength = distances[i + 1] - distances[i];

        if (segmentLength <= 1e-6f)
            return path[i + 1];

        float t = Mathf.Clamp01((progress - distances[i]) / segmentLength);
        
        // interpolate the correct inbetween
        return Vector3.Lerp(path[i], path[i + 1], t);
    }
}