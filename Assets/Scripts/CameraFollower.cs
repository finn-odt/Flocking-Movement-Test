using System;
using UnityEngine;

public class CameraFollower : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float zOffset = -10;
    [SerializeField] private float responseTime = 0.2f;

    private void Start()
    {
        transform.position = target.position + target.forward * zOffset;
        // look at target
        transform.rotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
    }

    void FixedUpdate()
    {
        Vector2 targetPos = new Vector2(target.position.x, target.position.y);
        Vector2 cameraPos = new Vector2(transform.position.x, transform.position.y);
        Vector2 offset = targetPos - cameraPos;
        float distance = offset.magnitude;

        if (distance < responseTime)
            return;
        
        transform.position += distance * Time.fixedDeltaTime * new Vector3(offset.x, offset.y, 0f);
    }
}