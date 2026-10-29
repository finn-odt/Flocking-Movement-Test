using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class LeaderMovement : MonoBehaviour
{
    [SerializeField] private FlockingAgentSpawner AgentSpawner;
    private Vector2 moveInput;

    [SerializeField] private float speed = 5.0f;
    [SerializeField] private float maxSpeed = 20.0f;
    [SerializeField, Range(1f, 2f)] private float reverseFactor = 1.5f;
    [SerializeField] private float airResistanceFactor = 3f;
    
    [SerializeField] private float dashFactor = 5.0f;
    [SerializeField, Tooltip("In seconds")] private float dashCooldown = 1.2f;
    private float timeOfLastDash = 0f;
    private bool dashing = false;

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
    public void OnDash(InputValue value)
    {
        if (!value.isPressed)
            return;

        if ((Time.time - timeOfLastDash) < dashCooldown)
            return;
        
        timeOfLastDash = Time.time;
        dashing = true;
    }

    void FixedUpdate()
    {
        float sign = Vector3.Dot(new Vector3(moveInput.x, moveInput.y, 0), rb.linearVelocity);
        if (moveInput.sqrMagnitude > 0 && (sign < 0 || rb.linearVelocity.magnitude < 20.0f))
        {
            rb.AddForce(speed * new Vector3(moveInput.x, moveInput.y, 0), ForceMode.Acceleration);
        }
        
        Vector3 input = new Vector3(moveInput.x, moveInput.y, 0f);
        Vector3 velocity = rb.linearVelocity;

        if (input.sqrMagnitude > 0f)  // significant input?
        {
            float dot = Vector3.Dot(input, velocity);

            float revFac = (dot < 0f ? reverseFactor : 1f);  // for reversing faster

            if (dashing)
            {
                dashing = false;
                revFac *= dashFactor;  // use reverse-factor to multiply dash-factor
            }
            
            // maximum speed check
            if (velocity.sqrMagnitude < maxSpeed * maxSpeed || dot < 0f)
                rb.AddForce(speed * revFac * input, ForceMode.Acceleration);
        }
        else if(velocity.sqrMagnitude > 0f)  // no input, but velocity
        {
            rb.linearVelocity -= airResistanceFactor * Time.deltaTime * velocity;
        }
    }
}