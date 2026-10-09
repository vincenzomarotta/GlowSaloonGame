using System.Collections.Generic;
using UnityEngine;

// Moves a character along waypoints and animates its model (walk bob, work wiggle, sit/lie pose).
public class Walker : MonoBehaviour
{
    public Transform model;
    public Renderer[] hair;
    public Renderer body;
    public Transform[] arms;
    public bool holding; // arms forward under a carried stack
    public float speed = 3f;

    readonly Queue<Vector3> path = new();
    Quaternion pose = Quaternion.identity;
    Vector3 lastPos;
    float baseY, phase, workUntil;

    public bool Arrived => path.Count == 0;

    public void Go(params Vector3[] points)
    {
        path.Clear();
        foreach (var p in points) path.Enqueue(p);
    }

    // Staff route through the central aisle (x = 0) so they never cut across furniture.
    public void GoViaAisle(Vector3 dest)
    {
        var p = transform.position;
        Go(new Vector3(0, 0, p.z), new Vector3(0, 0, dest.z), dest);
    }

    public void Pose(float y, Quaternion rot) { baseY = y; pose = rot; }
    public void Work(float seconds = 0.15f) => workUntil = Mathf.Max(workUntil, Time.time + seconds);
    public void SetHair(Color c) { foreach (var r in hair) r.sharedMaterial = Fx.Mat(c); }
    public void SetOutfit(Color c) => body.sharedMaterial = Fx.Mat(c);

    public void Face(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12 * Time.deltaTime);
    }

    void Start() => lastPos = transform.position;

    void Update()
    {
        float dt = Time.deltaTime;
        if (path.Count > 0)
        {
            var to = path.Peek() - transform.position;
            to.y = 0;
            float step = speed * dt;
            if (to.magnitude <= step) transform.position = path.Dequeue();
            else { transform.position += to.normalized * step; Face(to); }
        }

        float moved = (transform.position - lastPos).magnitude / Mathf.Max(dt, 1e-4f);
        lastPos = transform.position;

        var p = new Vector3(0, baseY, 0);
        var r = pose;
        float armL = 0, armR = 0;
        if (Time.time < workUntil && moved <= 0.2f)
        {
            // Busy hands in front, little bounce and sway: clearly working.
            phase += dt * 12;
            p.y += Mathf.Abs(Mathf.Sin(phase)) * 0.05f;
            r *= Quaternion.Euler(Mathf.Sin(phase * 0.5f) * 6, Mathf.Sin(phase * 0.35f) * 12, 0);
            armL = -75 + Mathf.Sin(phase * 1.6f) * 28;
            armR = -75 + Mathf.Cos(phase * 1.6f) * 28;
        }
        else if (moved > 0.2f)
        {
            phase += dt * 14;
            p.y += Mathf.Abs(Mathf.Sin(phase)) * 0.12f;
            r *= Quaternion.Euler(0, 0, Mathf.Sin(phase) * 5);
            armL = Mathf.Sin(phase) * 35;
            armR = -armL;
        }
        else phase = 0;
        if (holding) armL = armR = -70;

        float blend = 1 - Mathf.Exp(-20 * dt);
        model.localPosition = Vector3.Lerp(model.localPosition, p, blend);
        model.localRotation = Quaternion.Slerp(model.localRotation, r, blend);
        if (arms == null) return;
        arms[0].localRotation = Quaternion.Slerp(arms[0].localRotation, Quaternion.Euler(armL, 0, 0), blend);
        arms[1].localRotation = Quaternion.Slerp(arms[1].localRotation, Quaternion.Euler(armR, 0, 0), blend);
    }
}
