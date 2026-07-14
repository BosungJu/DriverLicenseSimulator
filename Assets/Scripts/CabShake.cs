using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CabShake : MonoBehaviour
{
    public Rigidbody carRb; //Car의 Rigidbody 연결

    [Header("흔들림 크기 (각도)")]
    public float idleShake = 0.15f; //공회전(정지) 시 흔들림

    public float roadShake = 0.5f; //고속에서 추가되는 노면 진동

    public float shakeSpeed = 15f; //흔들림 빠르기

    Quaternion baseRot;

    // Start is called before the first frame update
    void Start()
    {
        baseRot = transform.localRotation; //원래 방향 기억 (0,0,0)
    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.velocity.magnitude * 3.6f;

        //정지 땐 idleShake, 빠를수록 roadShake만큼 더 흔들리게
        float shakeAmount = idleShake + (speedKmh / 100f) * roadShake;

        //Perlin 노이즈 = 부드러운 랜덤 값 (-1 ~ 1)
        float t = Time.time * shakeSpeed;
        float nx = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f;

        //원래 방향에서 아주 살짝 흔들기
        transform.localRotation = baseRot * Quaternion.Euler(ny * shakeAmount, 0f, nx * shakeAmount);
    }
}
