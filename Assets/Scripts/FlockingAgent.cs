using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FlockingAgent : MonoBehaviour
{
    [SerializeField] private LayerMask flockLayer;
    public Transform flockLeader;
    public Rigidbody leaderRb;
    public Flock flock;
    private Rigidbody rb;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (!rb)
            return;
        
        Vector3 velocity = rb.linearVelocity;

        Vector3 separationForce = new Vector3(0.0f, 0.0f, 0.0f),
                alignmentForce = new Vector3(0.0f, 0.0f, 0.0f),
                cohesionForce = new Vector3(0.0f, 0.0f, 0.0f),
                leaderAttractionForce = new Vector3(0.0f, 0.0f, 0.0f);
        
        // SEPARATION
        float desiredDistance = flock.separationRadius;
        Vector3 separationSum = new Vector3(0.0f, 0.0f, 0.0f);
        int separationCount = 0;

        // ALIGNMENT
        Vector3 meanVelocity = new Vector3(0.0f, 0.0f, 0.0f);
        int peerCount = 0;

        // COHESION
        Vector3 meanPosition = new Vector3(0.0f, 0.0f, 0.0f);
        
        // nearest neighbours
        Collider[] peers;
        
        const int maxTries = 8;
        int i = 1;
        while (true)  // safety built-in [normally: never use while(true)]
        {
            peers = FindNearbyPeers(flock.sightRadius * i);
            if (peers.Length >= flock.minPeersInSight) break;
            
            i++;
            if (i > maxTries) break;
        }
        
        // Sort peers by distance to this Agent (nearest first) & cut off all superfluous peers
        System.Array.Sort(peers, (a, b) =>
        {
            float distA = (a.transform.position - transform.position).sqrMagnitude;
            float distB = (b.transform.position - transform.position).sqrMagnitude;

            return distA.CompareTo(distB);
        });
        
        // never more than X peers influence
        if (peers.Length > flock.maxPeersInSight)
            System.Array.Resize(ref peers, flock.maxPeersInSight);

        foreach (Collider peer in peers)
        {
            if (peer.attachedRigidbody == rb)
                continue;
            
            // normalized direction vector
            Vector3 direction = peer.transform.position - transform.position;  // from us to peer
            float distance = direction.sqrMagnitude;
            direction = Vector3.Normalize(direction);
            
            // SEPARATION
            if (distance < desiredDistance * desiredDistance) {  // using sqrMagnitude
                Vector3 sep = direction / distance;  // the closer, the higher the repulsion
                separationSum += -1 * sep;
                separationCount++;
            }
            
            // ALIGNMENT
            meanVelocity += peer.attachedRigidbody.linearVelocity;
            peerCount++;

            // COHESION
            meanPosition += peer.transform.position;
        }
        
        
        // steer = desired - velocity

        // SEPARATION
        if (separationCount > 0)
        {
            separationSum = separationSum / separationCount;
            separationSum = Vector3.Normalize(separationSum) * flock.maxSpeed;

            separationForce = Vector3.ClampMagnitude(separationSum - velocity, flock.maxForce);
        }

        if (peerCount > 0)
        {
            // ALIGNMENT
            meanVelocity = Vector3.Normalize(meanVelocity / peerCount) * flock.maxSpeed;
            // do not slow down, but try to hold maxSpeed while following the direction

            alignmentForce = Vector3.ClampMagnitude(meanVelocity - velocity, flock.maxForce);

            // COHESION
            meanPosition /= peerCount;
            Vector3 desiredDir = Vector3.Normalize(meanPosition - transform.position) * flock.maxSpeed;

            cohesionForce = Vector3.ClampMagnitude(desiredDir - velocity, flock.maxForce);
        }

        // LEADER ATTRACTION
        Vector3 leaderOffset = Vector3.Normalize(flockLeader.position - transform.position);
        float leaderDistance = Vector3.Distance(flockLeader.position, transform.position);
        Vector3 leaderVelocity = leaderRb.linearVelocity;
        
        float desiredSpeedLeader = Mathf.Min(leaderDistance * 2f, flock.maxSpeed);
        Vector3 desiredVelocityToLeader = leaderOffset * desiredSpeedLeader + leaderVelocity;
        
        // anti-proportional (more force, the further away)
        leaderAttractionForce = Vector3.ClampMagnitude(desiredVelocityToLeader - velocity, flock.maxForce);
        
        
        
        // apply different weights for different behaviour
        Vector3 steering =
            separationForce * flock.separationWeight
            + alignmentForce * flock.alignmentWeight
            + cohesionForce * flock.cohesionWeight
            + leaderAttractionForce * flock.leaderAttractionWeight;

        // eliminate vertical movement
        //steering = XMVectorMultiply( steering, XMVectorSet(1.0f, 0.0f, 1.0f, 0.0f) );

        // clamp to maximum force
        steering = Vector3.ClampMagnitude(steering, flock.maxForce);

        // apply force to physics-components for being used in the next step of Physics-calculation
        rb.AddForce(steering);
    }
    
    
    Collider[] FindNearbyPeers(float radius)
    {
        Collider[] peers = Physics.OverlapSphere(this.transform.position, radius, flockLayer);

        peers = System.Array.FindAll(peers, peer =>
        {
            if (!peer.TryGetComponent<FlockingAgent>(out var agent))
                return false;

            return agent != this && agent.flock == flock;
        });
        
        return peers;
    }
}