using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class LeaderManager : MonoBehaviour
{
    
    
    [SerializeField, Tooltip("NOT the Leader!")] private Transform flockParentObj;  // Stationary scene container, NOT the moving leader.
    [SerializeField] private int maxNumberOfLeaders = 16;
    [SerializeField] private List<FlockLeader> flockLeaders = new();
    [SerializeField] private GameObject agentPrefab;
    [SerializeField] private GameObject splitLeaderPrefab;
    
    [SerializeField] private float spawnRadius = 6.0f;
    [SerializeField] private bool spawnOnSphere = false;
    [SerializeField] private int numberOfAgents = 10;
    
    private float cellSize = 20.0f;  // always >= SightRadius (of Flock)
        
    private readonly List<Rigidbody> agents = new();
    private readonly List<Collider> agentColliders = new();
    private readonly List<int> agentFlockIds = new();
    private readonly List<int> agentLeaderIds = new();
    private readonly List<Flock> controlledFlocks = new();
    
    // FORMATION
    [Header("Formation")]
    [SerializeField] private Transform formationParentObj;
    [SerializeField] private GameObject formationPrefab;

    private SkinnedMeshRenderer formationSkinnedRenderer;
    private Mesh bakedMesh;
    private readonly List<Vector3> bakedVertexList = new();
    
    [SerializeField] private MeshFilter formationMeshFilter;
    [SerializeField] private List<Mesh> formationMeshes = new();
    [SerializeField] private uint samplingSeed = 12345u;
    [SerializeField, Min(0f)] private float formationStiffness = 8f;
    [SerializeField, Min(0f)] private float formationDamping = 5f;
    [SerializeField, Min(0f)] private float formationMaxAcceleration = 40f;
    
    // Formation Animations
    [SerializeField] private List<SkinnedMeshRenderer> formationMeshRenderers;
    // how to start the animation?
    // BakeMesh -> GetVertices
    //      -> how often? 10-15fps?
    
    private bool formationActive = false;
    public bool IsFormationActive => formationActive;
    
    [Header("Obstacle Avoidance")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float avoidanceRadius = 0.4f;
    [SerializeField] private float avoidanceDistance = 4f;
    [SerializeField] private float avoidanceStrength = 20f;
    
    // JOBS Parameters
    private NativeParallelMultiHashMap<int3, int> grid;
    
    private NativeArray<float3> positions;  // input
    private NativeArray<float3> velocities;  // input
    private NativeArray<int> flockIds;  // input
    private NativeArray<FlockSettings> flockSettingsById;  // input
    private NativeArray<NativeFlockLeader> flockLeaderById;  // input
    private NativeArray<float3> steeringForces;  // output
    
    private NativeArray<float3> formationTargetsLocal;
    private float4x4 previousFormationMatrix;
    
    private NativeArray<SurfaceAttachment> formationAttachments;
    private NativeArray<float3> animatedVertices;
    
    void Start()
    {
        controlledFlocks.Add(new Flock());

        if (flockLeaders.Count != 1)
        {
            Debug.Log("To start, only one flock leader needs to be configured in the flockLeaders-List !");
            return;
        }

        controlledFlocks[0].leaderId = 0;  // set leaderId of this flock
        
        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector3 initPos = flockLeaders[0].transform.position + (spawnOnSphere ? Random.onUnitSphere : Random.insideUnitSphere) * spawnRadius;
            GameObject o = Instantiate(agentPrefab, initPos, Quaternion.identity, flockParentObj);
            Rigidbody rb = o.GetComponent<Rigidbody>();
            if (rb != null)  // add agent to List for this Flock-object
                RegisterAgent(rb, 0);
            else
                Destroy(o);
        }

        InitializeNativeData();
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns>The ID of the created leader for the Flock-settings</returns>
    private int InitializeFlockLeader()
    {
        if (flockLeaders.Count >= maxNumberOfLeaders)
        {
            Debug.Log($"Cannot create anymore leaders. Maximum Capacity of {maxNumberOfLeaders} reached!");
            return -1;
        }
        
        Vector3 initPos = flockLeaders[0].transform.position + Random.onUnitSphere * spawnRadius * 0.5f;
        GameObject o = Instantiate(splitLeaderPrefab, this.transform);
        if (o != null)
        {
            ILeaderAgent agent = o.GetComponent<ILeaderAgent>();
            if (agent != null)
                agent.Manager = this;
            
            o.transform.position = initPos;
            // add agent to List for this Flock-object
            Rigidbody r = o.GetComponent<Rigidbody>();
            if (r != null)
            {
                flockLeaders.Add(new FlockLeader( o.transform, r ));
                return flockLeaders.Count - 1;
            }
            else
                Destroy(o);
        }

        return -1;
    }

    void CheckProjectileHit(Transform projectile, Vector3 offset, float affectionRadius, float deadlyRadius)
    {
        if (deadlyRadius >= affectionRadius)
        {
            Debug.LogError("The deadly radius of a projectile cannot be bigger than the overall affection radius.", this);
            return;
        }
        
        using var hitCount = new NativeReference<int>(Allocator.TempJob);
        
        // visualize explosion?

        var job = new CalculateHitUnitsJob
        {
            Positions = positions,
            HitCount = hitCount,  // output
            Grid = grid,
            CellSize = cellSize,
            ProjectileRadius = affectionRadius,
            DeadlyRadius = deadlyRadius,
            ProjectilePosition = projectile.position + offset,
            Seed = (uint)Time.frameCount
        };

        JobHandle handle = job.Schedule();
        handle.Complete();

        Debug.Log($"Hit agents: {hitCount.Value}");
    }
    
    private void RegisterAgent(Rigidbody rb, int flockId)
    {
        agents.Add(rb);
        agentFlockIds.Add(flockId);
        
        agentColliders.Add(rb.GetComponent<Collider>());
    }
    
    /// <summary>
    /// This method needs to be called, when a Leader or Flock is added
    /// </summary>
    private void InitializeNativeData()
    {
        int agentCount = agents.Count;
        int flockCount = controlledFlocks.Count;

        positions = new NativeArray<float3>(agentCount, Allocator.Persistent);
        velocities = new NativeArray<float3>(agentCount, Allocator.Persistent);
        steeringForces = new NativeArray<float3>(agentCount, Allocator.Persistent);
        flockIds = new NativeArray<int>(agentCount, Allocator.Persistent);
        flockSettingsById = new NativeArray<FlockSettings>(flockCount, Allocator.Persistent);
        flockLeaderById = new NativeArray<NativeFlockLeader>(maxNumberOfLeaders, Allocator.Persistent);
        formationTargetsLocal = new NativeArray<float3>(agentCount, Allocator.Persistent);
        formationAttachments = new NativeArray<SurfaceAttachment>(agentCount, Allocator.Persistent);
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
            LeaderId = flock.leaderId,
            
            MaxSpeed = flock.maxSpeed,
            MaxForce = flock.maxForce,
            SeparationMaxAcceleration = flock.separationMaxAcceleration,
            FollowingMaxAcceleration = flock.followingMaxAcceleration,
            FollowingMaxAccelerationInMotion = flock.followingMaxAccelerationInMotion,
            
            AttractionPositionWeight = flock.attractionPositionWeight,
            AttractionVelocityWeight = flock.attractionVelocityWeight,

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
    
    private void InitializeAnimatedFormation()
    {
        if (formationSkinnedRenderer == null)
            return;

        bakedMesh = new Mesh();

        int vertexCount = formationSkinnedRenderer.sharedMesh.vertexCount;

        animatedVertices = new NativeArray<float3>(vertexCount, Allocator.Persistent);
    }
    
    private void UpdateAnimatedVertices()
    {
        formationSkinnedRenderer.BakeMesh(bakedMesh);

        bakedMesh.GetVertices(bakedVertexList);

        for (int i = 0; i < bakedVertexList.Count; i++)
        {
            animatedVertices[i] = bakedVertexList[i];
        }
    }
    
    // Called automatically by Player Input
    public void OnFormation(InputValue value)
    {
        if (value.isPressed)
            ToggleFormation();
    }
    
    // Called automatically by Player Input
    public void OnHitCheck(InputValue value)
    {
        if (value.isPressed)
            CheckProjectileHit(flockLeaders[0].transform,  Random.onUnitSphere, 3f, 1f);
    }

    private bool isSplit = false;
    public void OnSplit()
    {
        if (!isSplit)
        {

            int leaderId = InitializeFlockLeader();
            if (leaderId < 0)
                return;

            isSplit = true;

            Flock newFlock = new Flock();
            newFlock.leaderId = leaderId;
            newFlock.maxSpeed = 70f;
            newFlock.maxForce = 35f;
            newFlock.separationMaxAcceleration = 15f;
            newFlock.followingMaxAcceleration = 40f;
            newFlock.followingMaxAccelerationInMotion = 65f;
            newFlock.attractionPositionWeight = 10f;
            newFlock.attractionVelocityWeight = 2f;
            newFlock.separationWeight = 0.1f;
            newFlock.alignmentWeight = 0.3f;
            newFlock.cohesionWeight = 0.01f;
            newFlock.leaderAttractionWeight = 5f;

            controlledFlocks.Add(newFlock);
            int idx = controlledFlocks.Count - 1;

            // change flock-belonging
            for (int i = agents.Count / 2; i < agents.Count; i++)
                agentFlockIds[i] = idx;
        }
        else
        {
            isSplit = false;
            
            // remove newest leader from existence
            Destroy(flockLeaders[flockLeaders.Count - 1].transform.gameObject);
            flockLeaders.RemoveAt(flockLeaders.Count - 1);

            int idx = controlledFlocks.Count - 2;
            // change flock-belonging
            for (int i = 0; i < agents.Count; i++)
                agentFlockIds[i] = idx;
            
            controlledFlocks.RemoveAt(controlledFlocks.Count - 1);  // remove newest flock
        }

        FreeData();
        InitializeNativeData();
    }

    [SerializeField] private Camera mainCamera;
    
    private bool drawingActive = false;
    private List<Vector3> slashLocations = new List<Vector3>();
    [SerializeField] private float minSlashPointDistance = 0.2f;
    
    [SerializeField] private LineRenderer slashRenderer;
    public IReadOnlyList<Vector3> SlashLocations => slashLocations;
    public bool DrawingActive => drawingActive;
    
    private readonly List<float> slashDistances = new();
    public IReadOnlyList<float> SlashDistances => slashDistances;
    public float SlashLength => slashDistances.Count > 0 ? slashDistances[^1] : 0f;
    
    private void Awake()
    {
        slashRenderer.useWorldSpace = true;
        slashRenderer.positionCount = 0;
        slashRenderer.enabled = false;
        
        if(formationParentObj != null)
            formationSkinnedRenderer = formationParentObj.GetComponentInChildren<SkinnedMeshRenderer>();
    }
    
    public void OnDraw(InputValue value)
    {
        if (!isSplit)  // only possible when split
            return;

        if (value.isPressed && !drawingActive)
            BeginSlash();
    }
    
    private void BeginSlash()
    {
        drawingActive = true;
        slashLocations.Clear();
        slashDistances.Clear();

        slashRenderer.positionCount = 0;
        slashRenderer.enabled = true;
    }
    
    private void FinishSlash()
    {
        drawingActive = false;

        // Process the actual slash attack here.
        // CalculateSlashHits(slashLocations);

        slashRenderer.enabled = false;
    }
    
    private void AddSlashPoint(Vector3 worldPosition)
    {
        if (slashLocations.Count == 0)
        {
            slashLocations.Add(worldPosition);
            slashDistances.Add(0f);
        }
        if (slashLocations.Count > 0)
        {
            Vector3 previous = slashLocations[^1];

            float segmentLength = Vector3.Distance(previous, worldPosition);

            if (segmentLength < minSlashPointDistance)
                return;

            slashLocations.Add(worldPosition);
            slashDistances.Add(slashDistances[^1] + segmentLength);  // accumulator for distance
        }
        
        // Update the LineRenderer
        int index = slashLocations.Count - 1;
        slashRenderer.positionCount = slashLocations.Count;
        slashRenderer.SetPosition(index, worldPosition);
    }
    
    private void Update()
    {
        if (!drawingActive)
            return;

        // split was ended or mouse is not pressed anymore?
        if (!isSplit || !Mouse.current.leftButton.isPressed)
        {
            FinishSlash();
            return;
        }

        Vector2 screenPosition = Mouse.current.position.ReadValue();

        Plane movementPlane = new Plane(Vector3.back, Vector3.zero);

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        if (movementPlane.Raycast(ray, out float distance))
            AddSlashPoint(ray.GetPoint(distance));
    }
    
    /// <summary>
    /// Sample one target per agent. Called once per formation activation / mesh change.
    /// Mesh must be readable and must be rendered by formationMeshFilter.
    /// </summary>
    public void StartFormation()
    {
        if (!positions.IsCreated)
        {
            Debug.LogError("Initialize agents before starting a formation.", this);
            return;
        }
        
        Mesh mesh = formationSkinnedRenderer.sharedMesh;
        
        if (formationMeshFilter == null || mesh == null || !mesh.isReadable)
        {
            Debug.LogError("Assign a Formation MeshFilter and a readable mesh (import-configs).", this);
            return;
        }

        InitializeAnimatedFormation();
        
        var meshVertices = new List<Vector3>(mesh.vertexCount);
        mesh.GetVertices(meshVertices);

        var validTriangles = new List<int3>();
        var areaCdf = new List<float>();  // cdf = cumulative distribution function
        var submeshIndices = new List<int>();
        float totalArea = 0f;

        // One-time mesh preparation | costly per-agent sampling runs in Burst afterwards
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            // only sample triangle-meshes
            if (mesh.GetTopology(s) != MeshTopology.Triangles)
                continue;

            // iterate through all triangles/faces
            // [i, i+1, i+2] form a triangle -> submeshIndices contains vertices
            mesh.GetTriangles(submeshIndices, s);
            for (int i = 0; i + 2 < submeshIndices.Count; i += 3)
            {
                int a = submeshIndices[i];
                int b = submeshIndices[i + 1];
                int c = submeshIndices[i + 2];
                
                // FASCINATING! ( |AB| * |AC| * sin(a) ) / 2 =  ( |AB| * ( |AC| * sin(a) ) ) / 2 = (g * h) / 2 = A(Triangle)
                float area = Vector3.Cross(meshVertices[b] - meshVertices[a], meshVertices[c] - meshVertices[a]).magnitude * 0.5f;

                if (area <= 1e-8f)
                    continue;

                totalArea += area;
                validTriangles.Add(new int3(a, b, c));
                areaCdf.Add(totalArea);
            }
        }

        if (validTriangles.Count == 0)
        {
            Debug.LogError("Formation mesh has no non-degenerate triangles.", this);
            return;
        }

        var vertices = new NativeArray<float3>(meshVertices.Count, Allocator.TempJob);
        var triangles = new NativeArray<int3>(validTriangles.Count, Allocator.TempJob);
        var cumulativeAreas = new NativeArray<float>(areaCdf.Count, Allocator.TempJob);

        try
        {
            for (int i = 0; i < meshVertices.Count; i++)
            {
                Vector3 v = meshVertices[i];
                vertices[i] = new float3(v.x, v.y, v.z);
            }

            for (int i = 0; i < validTriangles.Count; i++)
            {
                triangles[i] = validTriangles[i];
                cumulativeAreas[i] = areaCdf[i];
            }

            var sampleJob = new SampleFormationPointsJob
            {
                Vertices = vertices,
                Triangles = triangles,
                CumulativeAreas = cumulativeAreas,
                TotalArea = totalArea,
                LocalTargets = formationTargetsLocal,
                Attachments = formationAttachments,
                Seed = samplingSeed
            };

            sampleJob.Schedule(agents.Count, 64).Complete();
        }
        finally
        {
            vertices.Dispose();
            triangles.Dispose();
            cumulativeAreas.Dispose();
        }

        // Make sure displayed and sampled geometry match.
        previousFormationMatrix = ToFloat4x4(formationSkinnedRenderer.transform.localToWorldMatrix);
        formationActive = true;
        
        // deactivate colliders shortly to enable them quick forming
        //SetAgentCollisions(false);
    }
    
    private void SetAgentCollisions(bool enabled)
    {
        for (int i = 0; i < agentColliders.Count; i++)
        {
            agentColliders[i].enabled = enabled;
        }
    }
    
    private static float4x4 ToFloat4x4(Matrix4x4 m)
    {
        return new float4x4(
            new float4(m.m00, m.m10, m.m20, m.m30),
            new float4(m.m01, m.m11, m.m21, m.m31),
            new float4(m.m02, m.m12, m.m22, m.m32),
            new float4(m.m03, m.m13, m.m23, m.m33)
        );
    }
    
    public void StartFormationByIndex(int index)
    {
        if (index < 0 || index >= formationMeshes.Count)
        {
            Debug.LogWarning("No formation mesh at list index " + index, this);
            return;
        }

        StartFormation(formationMeshes[index]);
    }

    public void ToggleFormation()
    {
        if (formationActive)
            StopFormation();
        else
            StartFormationByIndex(0);
    }

    public void StopFormation()
    {
        formationActive = false;
        
        if(bakedMesh != null)
            bakedMesh.Clear();
        if(animatedVertices.IsCreated) 
            animatedVertices.Dispose();
    }
    
    private void FixedUpdate()
    {
        if (!positions.IsCreated || agents.Count == 0)
            return;
        
        // Capture agent physics state
        for (int i = 0; i < agents.Count; i++)
        {
            positions[i] = agents[i].position;
            velocities[i] = agents[i].linearVelocity;
            flockIds[i] = agentFlockIds[i];
        }
        
        // capture leader physics state
        for (int i = 0; i < math.min(maxNumberOfLeaders, flockLeaders.Count); i++)
        {
            flockLeaderById[i] = new NativeFlockLeader
            {
                Position = flockLeaders[i].transform.position,
                Velocity = flockLeaders[i].rigidbody.linearVelocity
            };
        }
        
        // Schedule Jobs
        JobHandle handle;
        if (formationActive)
        {
            UpdateAnimatedVertices();

            var updateJob = new UpdateAnimatedFormationTargetsJob
            {
                AnimatedVertices = animatedVertices,
                Attachments = formationAttachments,
                LocalTargets = formationTargetsLocal
            };

            JobHandle updateHandle = updateJob.Schedule(agents.Count, 64);

            handle = ScheduleFormation(updateHandle);
        }
        else
        {
            grid.Clear();
            JobHandle build = ScheduleSpatialGridBuild();
            handle = ScheduleFlocking(build);
        }

        handle.Complete(); // wait for Jobs to end to use results
        
        

        for (int i = 0; i < agents.Count; i++)
        {
            Rigidbody rb = agents[i];

            Vector3 steering = steeringForces[i];
            
            Vector3 avoidance = CalculateObstacleAvoidance(rb.position, rb.linearVelocity, steering);

            if (formationActive)
            {
                rb.AddForce(steering + avoidance, ForceMode.Acceleration);
            }
            else
            {
                rb.AddForce(steering, ForceMode.Force);
                rb.AddForce(avoidance, ForceMode.Acceleration);
            }
        }
    }
    
    private Vector3 CalculateObstacleAvoidance(Vector3 position, Vector3 velocity, Vector3 steering)
    {
        Vector3 desired = velocity + steering * Time.fixedDeltaTime;

        if (desired.sqrMagnitude < 0.001f)
            return Vector3.zero;

        Vector3 direction = desired.normalized;

        if (!Physics.SphereCast(position, avoidanceRadius, direction, out RaycastHit hit, avoidanceDistance, obstacleMask, QueryTriggerInteraction.Ignore))
            return Vector3.zero;

        // Remove the velocity component pointing into the obstacle
        Vector3 tangent = Vector3.ProjectOnPlane(direction, hit.normal);

        float urgency = 1f - hit.distance / avoidanceDistance;
        Vector3 desiredDirection = tangent.sqrMagnitude > 0.001f ? tangent.normalized : hit.normal;
        Vector3 correction = desiredDirection - direction;

        return avoidanceStrength * urgency * correction;
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

    private JobHandle ScheduleFlocking(JobHandle dependency)
    {
        var queryJob = new CalculateSteeringForceJob()
        {
            Positions = positions,
            Velocities  = velocities,
            FlockIds = flockIds,
            FlockSettingsById = flockSettingsById,
            Grid = grid,
            SteeringForces = steeringForces,
            Leaders = flockLeaderById,
            CellSize = cellSize,
            MaxNeighbors = 7
        };

        return queryJob.Schedule(positions.Length, 64, dependency);
    }

    private JobHandle ScheduleFormation(JobHandle dependency)
    {
        float4x4 currentFormationMatrix = ToFloat4x4(formationMeshFilter.transform.localToWorldMatrix);
        
        var queryJob = new CalculateFormationForceJob()
        {
            Positions = positions,
            Velocities  = velocities,
            LocalTargets = formationTargetsLocal,
            PreviousLocalTargets = ,
            SteeringForces = steeringForces,
            CurrentLocalToWorld = currentFormationMatrix,
            PreviousLocalToWorld = previousFormationMatrix,
            FixedDeltaTime = Time.fixedDeltaTime,
            Stiffness = formationStiffness,
            Damping = formationDamping,
            MaxAcceleration = formationMaxAcceleration
        };

        JobHandle handle = queryJob.Schedule(agents.Count, 64, dependency);
        
        previousFormationMatrix = currentFormationMatrix;
        return handle;
    }
    
    private void OnDestroy()
    {
        FreeData();
    }

    private void FreeData()
    {
        if (positions.IsCreated) positions.Dispose();
        if (velocities.IsCreated) velocities.Dispose();
        if (flockIds.IsCreated) flockIds.Dispose();
        if (flockSettingsById.IsCreated) flockSettingsById.Dispose();
        if (flockLeaderById.IsCreated) flockLeaderById.Dispose();
        if (steeringForces.IsCreated) steeringForces.Dispose();
        if (formationTargetsLocal.IsCreated) formationTargetsLocal.Dispose();
        if (formationAttachments.IsCreated) formationAttachments.Dispose();
        //if (animatedVertices.IsCreated) animatedVertices.Dispose();
        if (grid.IsCreated) grid.Dispose();
    }

}