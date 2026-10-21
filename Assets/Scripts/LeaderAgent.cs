using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class LeaderAgent : MonoBehaviour
{
    
    [SerializeField] private Transform flockParentObj;
    [SerializeField] private Transform flockLeader;
    [SerializeField] private Rigidbody flockLeaderRB;
    [SerializeField] private GameObject agentPrefab;
    
    [SerializeField] private float spawnRadius = 6.0f;
    [SerializeField] private bool spawnOnSphere = false;
    [SerializeField] private int numberOfAgents = 10;
        
    private readonly List<Rigidbody> agents = new();
    private readonly List<int> agentFlockIds = new();
    private readonly List<Flock> controlledFlocks = new();
    
    // JOBS Parameters
    private NativeParallelMultiHashMap<int3, int> grid;
    
    private float cellSize = 8.0f;  // always >= SightRadius (of Flock)
    
    private NativeArray<float3> positions;  // input
    private NativeArray<float3> velocities;  // input
    private NativeArray<int> flockIds;  // input
    private NativeArray<FlockSettings> flockSettingsById;  // input
    private NativeArray<float3> steeringForces;  // output
    
    void Start()
    {
        controlledFlocks.Add(new Flock());
        
        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector3 initPos = flockLeader.position + (spawnOnSphere ? Random.onUnitSphere : Random.insideUnitSphere) * spawnRadius;
            GameObject o = Instantiate(agentPrefab, flockParentObj);
            if (o != null)
            {
                o.transform.position = initPos;
                // add agent to List for this Flock-object
                Rigidbody r = o.GetComponent<Rigidbody>();
                if (r != null)
                    RegisterAgent(r, 0);
                else
                    Destroy(o);
            }
        }

        InitializeNativeData();
    }
    
    private void RegisterAgent(Rigidbody rb, int flockId)
    {
        agents.Add(rb);
        agentFlockIds.Add(flockId);
    }
    
    private void InitializeNativeData()
    {
        int agentCount = agents.Count;
        int flockCount = controlledFlocks.Count;

        positions = new NativeArray<float3>(agentCount, Allocator.Persistent);
        velocities = new NativeArray<float3>(agentCount, Allocator.Persistent);
        steeringForces = new NativeArray<float3>(agentCount, Allocator.Persistent);
        flockIds = new NativeArray<int>(agentCount, Allocator.Persistent);
        flockSettingsById = new NativeArray<FlockSettings>(flockCount, Allocator.Persistent);
        grid = new NativeParallelMultiHashMap<int3, int>(agentCount, Allocator.Persistent);

        UpdateFlockSettings();
    }
    
    private void UpdateFlockSettings()
    {
        for (int i = 0; i < controlledFlocks.Count; i++)
            flockSettingsById[i] = ToSettings(controlledFlocks[i]);
    }
    
    private static FlockSettings ToSettings(Flock flock)
    {
        return new FlockSettings
        {
            MaxSpeed = flock.maxSpeed,
            MaxForce = flock.maxForce,

            SightRadius = flock.sightRadius,
            SeparationRadius = flock.separationRadius,

            SeparationWeight = flock.separationWeight,
            AlignmentWeight = flock.alignmentWeight,
            CohesionWeight = flock.cohesionWeight,
            LeaderAttractionWeight = flock.leaderAttractionWeight,

            MinPeers = flock.minPeersInSight,
            MaxPeers = flock.maxPeersInSight
        };
    }
    
    
    
    private void FixedUpdate()
    {
        // 1. Capture agent physics state
        for (int i = 0; i < agents.Count; i++)
        {
            positions[i] = agents[i].position;
            velocities[i] = agents[i].linearVelocity;
            flockIds[i] = agentFlockIds[i];
        }

        grid.Clear();
        
        // 2. Schedule spatial grid and steering jobs
        JobHandle buildHandle = ScheduleSpatialGridBuild();

        JobHandle flockingHandle = ScheduleFlocking(buildHandle);

        flockingHandle.Complete();  // wait for Jobs to end to use results

        // 3. Apply the calculated steering forces
        for (int i = 0; i < agents.Count; i++)
        {
            agents[i].AddForce(steeringForces[i]);
        }
    }
    
    
    
    private JobHandle ScheduleSpatialGridBuild()
    {
        var buildJob = new BuildGridJob
        {
            Positions = positions,
            Grid = grid.AsParallelWriter(),
            CellSize = cellSize
        };

        return buildJob.Schedule(positions.Length, 64);
    }

    private JobHandle ScheduleFlocking(JobHandle buildHandle)
    {
        var queryJob = new CalculateSteeringForceJob()
        {
            Positions = positions,
            Velocities  = velocities,
            FlockIds = flockIds,
            FlockSettingsById = flockSettingsById,
            Grid = grid,
            SteeringForces = steeringForces,
            LeaderPosition = flockLeader.position,
            LeaderVelocity =  flockLeaderRB.linearVelocity,
            CellSize = cellSize,
            MaxNeighbors = 7
        };

        return queryJob.Schedule(positions.Length, 64, buildHandle);
    }
    
    private void OnDestroy()
    {
        if (positions.IsCreated)
            positions.Dispose();

        if (velocities.IsCreated)
            velocities.Dispose();

        if (flockIds.IsCreated)
            flockIds.Dispose();

        if (flockSettingsById.IsCreated)
            flockSettingsById.Dispose();

        if (steeringForces.IsCreated)
            steeringForces.Dispose();

        if (grid.IsCreated)
            grid.Dispose();
    }

}