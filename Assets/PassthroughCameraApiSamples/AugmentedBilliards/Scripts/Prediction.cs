using PassthroughCameraSamples.MultiObjectDetection; // access DetectionManager
using UnityEngine;
using System.Collections.Generic;

public class Prediction : MonoBehaviour
{
    [Header("Reference to DetectionManager in scene")]
    public DetectionManager detectionManager;
    [Header("Prediction Settings")]
    private float ballRadius = 0.0122136752136752f; // Kittec added - ball radius in UV space
    public Color trajectoryColor = Color.yellow; // Kittec added - trajectory line color
    public float lineWidth = 0.01f; // Kittec added - trajectory line width

    private List<List<Vector2>> predictedTrajectories = new List<List<Vector2>>();
    private Material trajectoryMaterial;

    // ballRadius = state.Value.ballRadiusRatio;
    private void Start()
    {
        trajectoryMaterial = new Material(Shader.Find("Unlit/Color"));
        trajectoryMaterial.color = trajectoryColor;
    }
    void Update()
    {
        if (detectionManager == null) return;

        // get current normalized table state
        var state = detectionManager.GetCurrentTableState();
        if (state == null) return; // nothing yet
        ballRadius = state.Value.ballRadiusRatio;
        // TODO: prediction
        // Kittec added - start prediction calculation
        PredictTrajectories(state.Value);

        // visualize it back in world space
        detectionManager.RenderNormalizedState(state.Value, predictedTrajectories);

        // Kittec added - render predicted trajectories
        // RenderPredictedTrajectories(state.Value);
    }
    // Kittec added - trajectory prediction

    private void PredictTrajectories(DetectionManager.TableState state)
    {
        predictedTrajectories.Clear();

        if (state.Balls == null || state.Balls.Count == 0) return;

        Vector2 stickPos = state.StickPos;
        Vector2 stickDir = state.StickDir.normalized;

        // Kittec added - find the first ball that intersects with the aiming line
        int targetBallIndex = -1;
        float minIntersectDistance = float.MaxValue;
        Vector2 actualHitPoint = Vector2.zero;  // Store the actual collision point

        // Check each ball for intersection with the stick's aiming line
        for (int i = 0; i < state.Balls.Count; i++)
        {
            Vector2 ballPos = state.Balls[i].UV;

            // Calculate vector from stick position to ball
            Vector2 ballToStick = ballPos - stickPos;

            // Project this vector onto stick direction
            float dotProduct = Vector2.Dot(ballToStick, stickDir);

            // Only consider balls in front of the stick
            if (dotProduct <= 0) continue;

            // Find the closest point on the stick line to the ball center
            Vector2 closestPoint = stickPos + stickDir * dotProduct;

            // Calculate perpendicular distance from ball center to stick line
            float perpDistance = Vector2.Distance(ballPos, closestPoint);

            // If the perpendicular distance is less than ball radius, 
            // the stick line intersects with this ball
            if (perpDistance < ballRadius)
            {
                // Calculate the intersection point(s) with the ball
                // Using Pythagorean theorem: r² = d² + x²
                // Where r is ball radius, d is perpendicular distance, 
                // and x is the distance from closest point to intersection
                float intersectionOffset = Mathf.Sqrt(ballRadius * ballRadius - perpDistance * perpDistance);

                // The first intersection point (entering the ball)
                float intersectionDistance = dotProduct - intersectionOffset;

                // Find the ball that is hit first (closest to stick position)
                if (intersectionDistance > 0 && intersectionDistance < minIntersectDistance)
                {
                    minIntersectDistance = intersectionDistance;
                    targetBallIndex = i;

                    // Calculate and store the actual hit point on the ball surface
                    Vector2 intersectionPoint = stickPos + stickDir * intersectionDistance;
                    actualHitPoint = intersectionPoint;
                }
            }
        }

        // If no target ball found, return
        if (targetBallIndex < 0) return;

        // Get ball position (center)
        Vector2 cueBallPos = state.Balls[targetBallIndex].UV;

        // Kittec added - calculate hit effect using the actual collision point
        // The impact direction is from hit point to ball center
        Vector2 impactNormal = (cueBallPos - actualHitPoint).normalized;

        // Calculate initial direction after hit
        Vector2 initialDir;

        // The distance from hit point to ball center line
        float offsetFromCenter = Vector2.Distance(actualHitPoint, cueBallPos);
        float normalizedOffset = offsetFromCenter / ballRadius; // 0 = center, 1 = edge

        // If hit is very close to center line, use stick direction
        // if (normalizedOffset < 1.0f) // 10% from center
        // {
        //     initialDir = stickDir;
        // }
        // else
        // {
        //     // For off-center hits, the ball moves in the direction of the impact normal
        //     initialDir = impactNormal;
        // }
        initialDir = stickDir;

        // Create trajectory list and add initial hit point
        List<Vector2> initialTrajectory = new List<Vector2> { cueBallPos };

        // Simulate ball trajectory
        SimulateBallTrajectory(state, cueBallPos, initialDir, initialTrajectory, 0);
    }

    // Kittec added - ball trajectory simulation
    private void SimulateBallTrajectory(DetectionManager.TableState state, Vector2 ballPos, Vector2 direction,
                                        List<Vector2> trajectory, int collisionCount)
    {
        if (collisionCount >= 2)
        {
            Vector2 finalPos = ExtrapolatePosition(ballPos, direction, 2.0f);
            trajectory.Add(finalPos);
            predictedTrajectories.Add(new List<Vector2>(trajectory));
            return;
        }

        float timeToWallCollision = float.MaxValue;
        Vector2 wallNormal = Vector2.zero;
        Vector2 wallCollisionPoint = Vector2.zero;

        CheckWallCollision(ballPos, direction, state.TableSize, ref timeToWallCollision,
                          ref wallCollisionPoint, ref wallNormal);

        float timeToBallCollision = float.MaxValue;
        int hitBallIndex = -1;
        Vector2 ballCollisionPoint = Vector2.zero;
        Vector2 ballHitNormal = Vector2.zero;

        for (int i = 0; i < state.Balls.Count; i++)
        {
            Vector2 otherBallPos = state.Balls[i].UV;

            if (Vector2.Distance(otherBallPos, ballPos) < 0.001f) continue;

            float time;
            Vector2 collisionPoint, normal;

            if (CheckBallCollision(ballPos, direction, otherBallPos, out time, out collisionPoint, out normal) &&
                time < timeToBallCollision)
            {
                timeToBallCollision = time;
                hitBallIndex = i;
                ballCollisionPoint = collisionPoint;
                ballHitNormal = normal;
            }
        }

        if (timeToWallCollision < timeToBallCollision && timeToWallCollision < float.MaxValue)
        {
            trajectory.Add(wallCollisionPoint);

            Vector2 reflectedDir = Vector2.Reflect(direction, wallNormal).normalized;

            SimulateBallTrajectory(state, wallCollisionPoint, reflectedDir, trajectory, collisionCount + 1);
        }
        else if (timeToBallCollision < float.MaxValue)
        {
            trajectory.Add(ballCollisionPoint);

            // Kittec added - elastic collision physics
            Vector2 hitBallPos = state.Balls[hitBallIndex].UV;

            Vector2 collisionNormal = (ballCollisionPoint - hitBallPos).normalized;

            float vDotN = Vector2.Dot(direction, collisionNormal);
            Vector2 vNormal = vDotN * collisionNormal;
            Vector2 vTangent = direction - vNormal;

            Vector2 hitBallNewVelocity = vNormal;

            Vector2 newDirection = vTangent.normalized;

            SimulateBallTrajectory(state, ballCollisionPoint, newDirection, trajectory, collisionCount + 1);

            if (hitBallIndex >= 0 && collisionCount < 2)
            {
                List<Vector2> hitBallTrajectory = new List<Vector2> { hitBallPos };
                SimulateBallTrajectory(state, hitBallPos, hitBallNewVelocity.normalized, hitBallTrajectory, collisionCount);
            }
        }
        else
        {
            Vector2 finalPos = ExtrapolatePosition(ballPos, direction, 2.0f);
            trajectory.Add(finalPos);
            predictedTrajectories.Add(new List<Vector2>(trajectory));
        }
    }

    // Kittec added - wall collision detection
    private void CheckWallCollision(Vector2 ballPos, Vector2 direction, Vector2 tableSize,
                                   ref float minTime, ref Vector2 collisionPoint, ref Vector2 normal)
    {
        float timeLeft = (ballRadius - ballPos.x) / direction.x;
        if (timeLeft > 0 && timeLeft < minTime &&
            InRange(ballPos.y + direction.y * timeLeft, 0, tableSize.y))
        {
            minTime = timeLeft;
            collisionPoint = new Vector2(ballRadius, ballPos.y + direction.y * timeLeft);
            normal = Vector2.right;
        }

        float timeRight = (tableSize.x - ballRadius - ballPos.x) / direction.x;
        if (timeRight > 0 && timeRight < minTime &&
            InRange(ballPos.y + direction.y * timeRight, 0, tableSize.y))
        {
            minTime = timeRight;
            collisionPoint = new Vector2(tableSize.x - ballRadius, ballPos.y + direction.y * timeRight);
            normal = Vector2.left;
        }

        float timeBottom = (ballRadius - ballPos.y) / direction.y;
        if (timeBottom > 0 && timeBottom < minTime &&
            InRange(ballPos.x + direction.x * timeBottom, 0, tableSize.x))
        {
            minTime = timeBottom;
            collisionPoint = new Vector2(ballPos.x + direction.x * timeBottom, ballRadius);
            normal = Vector2.up;
        }

        float timeTop = (tableSize.y - ballRadius - ballPos.y) / direction.y;
        if (timeTop > 0 && timeTop < minTime &&
            InRange(ballPos.x + direction.x * timeTop, 0, tableSize.x))
        {
            minTime = timeTop;
            collisionPoint = new Vector2(ballPos.x + direction.x * timeTop, tableSize.y - ballRadius);
            normal = Vector2.down;
        }
    }

    // Kittec added - ball collision detection
    private bool CheckBallCollision(Vector2 ball1Pos, Vector2 ball1Dir, Vector2 ball2Pos,
                                   out float time, out Vector2 collisionPoint, out Vector2 normal)
    {
        time = float.MaxValue;
        collisionPoint = Vector2.zero;
        normal = Vector2.zero;

        float combinedRadius = ballRadius * 2;

        Vector2 relativePos = ball1Pos - ball2Pos;
        float a = Vector2.Dot(ball1Dir, ball1Dir);
        float b = 2 * Vector2.Dot(relativePos, ball1Dir);
        float c = Vector2.Dot(relativePos, relativePos) - combinedRadius * combinedRadius;

        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0) return false;

        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDiscriminant) / (2 * a);
        float t2 = (-b + sqrtDiscriminant) / (2 * a);

        if (t1 > 0) time = t1;
        else if (t2 > 0) time = t2;
        else return false;

        collisionPoint = ball1Pos + ball1Dir * time;
        normal = (collisionPoint - ball2Pos).normalized;

        return true;
    }

    // Kittec added - utility for range check
    private bool InRange(float value, float min, float max)
    {
        return value >= min && value <= max;
    }

    // Kittec added - utility for position extrapolation
    private Vector2 ExtrapolatePosition(Vector2 startPos, Vector2 direction, float distance)
    {
        return startPos + direction * distance;
    }



    // Kittec added - trajectory rendering
    // private void RenderPredictedTrajectories(DetectionManager.TableState state)
    // {
    //     if (predictedTrajectories.Count == 0) return;
    // 
    //     if (!TableCoordinateUtil.TryBuildBasis(detectionManager.m_anchorManager.getBounds(), 
    //                                         out TableCoordinateUtil.TableBasis tb))
    //         return;
    // 
    //     void RenderTrajectory(List<Vector2> trajectory)
    //     {
    //         if (trajectory.Count < 2) return;
    //         
    //         GameObject lineObj = new GameObject("PredictionLine");
    //         LineRenderer lr = lineObj.AddComponent<LineRenderer>();
    //         lr.positionCount = trajectory.Count;
    //         lr.startWidth = lr.endWidth = lineWidth;
    //         lr.material = trajectoryMaterial;
    //         lr.useWorldSpace = true;
    //         
    //         for (int i = 0; i < trajectory.Count; i++)
    //         {
    //             Vector3 worldPos = tb.UVToWorld(trajectory[i]);
    //             lr.SetPosition(i, worldPos);
    //         }
    //         
    //         detectionManager.m_predictLines.Add(lr);
    //     }
    // 
    //     foreach (var trajectory in predictedTrajectories)
    //     {
    //         RenderTrajectory(trajectory);
    //     }
    // }
}
