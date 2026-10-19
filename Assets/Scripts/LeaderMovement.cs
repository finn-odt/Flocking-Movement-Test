using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class LeaderMovement : MonoBehaviour
{
    [SerializeField] private FlockingAgentSpawner AgentSpawner;
    private Vector2 moveInput;

    [SerializeField] private float speed = 5.0f;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    
    // Called automatically by Player Input
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }
    
    // Called automatically by Player Input
    public void OnSplit(InputValue value)
    {
        if (value.isPressed)
        {
            AgentSpawner.Split();
        }
    }

    void Update()
    {
        if (moveInput.sqrMagnitude > 0)
        {
            rb.AddForce(speed * new Vector3(moveInput.x, moveInput.y, 0), ForceMode.Acceleration);
        }
    }
}