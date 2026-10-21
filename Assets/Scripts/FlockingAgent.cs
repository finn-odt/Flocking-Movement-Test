using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FlockingAgent : MonoBehaviour
{
    private Rigidbody rb;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }
}