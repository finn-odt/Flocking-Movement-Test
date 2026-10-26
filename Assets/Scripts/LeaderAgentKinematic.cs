using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Rigidbody))]
public class LeaderAgentKinematic : MonoBehaviour, ILeaderAgent
{
    public LeaderManager Manager { get; set; }
    
    private Rigidbody rb;  // should be configured isKinematic=true

    private float pathProgress;
    private bool pathFinished;
    private bool wasDrawing;
    
    private int currentSegment;
    
    [SerializeField] private float moveSpeed = 15f;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }


    private void FixedUpdate()
    {
        // if(manager == null) is expensive...
        
        // Detect the beginning of a new slash
        if (Manager.DrawingActive && !wasDrawing)
        {
            pathProgress = 0f;
            currentSegment = 0;
            pathFinished = false;
        }
        
        wasDrawing = Manager.DrawingActive;
        
        var path = Manager.SlashLocations;
        
        if (path.Count < 2 || pathFinished)
            return;

        Vector3 newPos = GetNextPosition();
        rb.MovePosition(newPos);
        
        // Only finish when the player has stopped drawing
        // and we've reached the final point.
        if (!Manager.DrawingActive && pathProgress >= Manager.SlashLength - 0.001f)
            pathFinished = true;
    }
    
    private Vector3 GetNextPosition()
    {
        var path = Manager.SlashLocations;
        var distances = Manager.SlashDistances;

        if (path.Count < 2 || distances.Count != path.Count)
            return rb.position;

        // Move forward by a fixed world-space distance [limit: SlashLength]
        pathProgress = Mathf.Min(
            pathProgress + moveSpeed * Time.fixedDeltaTime,
            Manager.SlashLength
        );

        // skip segments until the expected progress is reached
        while (currentSegment < path.Count - 2 && distances[currentSegment + 1] < pathProgress)
            currentSegment++;

        // Interpolate within the current segment
        float segmentStart = distances[currentSegment];
        float segmentEnd = distances[currentSegment + 1];

        float segmentLength = segmentEnd - segmentStart;

        if (segmentLength <= 1e-6f)
            return path[currentSegment + 1];

        // how far do we need to travel in this segment
        float t = Mathf.Clamp01((pathProgress - segmentStart) / segmentLength);

        return Vector3.Lerp(path[currentSegment], path[currentSegment + 1], t);
    }
}