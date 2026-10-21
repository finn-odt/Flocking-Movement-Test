using System.Collections.Generic;
using UnityEngine;

public class FlockingAgentSpawner : MonoBehaviour
{
    [SerializeField] private Transform flockLeader;
    [SerializeField] private Rigidbody flockLeaderRB;
    [SerializeField] private GameObject agentPrefab;

    private List<Transform> agents = new List<Transform>();
    
    [SerializeField] private float spawnRadius = 6.0f;
    [SerializeField] private bool spawnOnSphere = false;
    [SerializeField] private int numberOfAgents = 10;
    
    private List<Flock> controlledFlocks = new List<Flock>();
    
    void Start()
    {
        controlledFlocks.Add(new Flock());
        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector3 initPos = flockLeader.position + (spawnOnSphere ? Random.onUnitSphere : Random.insideUnitSphere) * spawnRadius;
            GameObject o = Instantiate(agentPrefab, this.transform);
            if (o != null)
            {
                o.transform.position = initPos;
                agents.Add(o.transform);
                FlockingAgent f = o.GetComponent<FlockingAgent>();
            }
        }
    }

    private bool isSplit = false;
    public void Split()
    {
        if (isSplit)
            return;

        isSplit = true;
        
        controlledFlocks.Add(new Flock());
        
        for (int i = 0; i < numberOfAgents; i++)
        {
            // spawn around current leader of main flock
            Vector3 initPos = flockLeader.position + (spawnOnSphere ? Random.onUnitSphere : Random.insideUnitSphere) * spawnRadius;
            GameObject o = Instantiate(agentPrefab, this.transform);
            if (o != null)
            {
                o.transform.position = initPos;
                agents.Add(o.transform);
                FlockingAgent f = o.GetComponent<FlockingAgent>();
            }
        }
    }
}