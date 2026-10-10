using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CabShake : MonoBehaviour
{
    public Rigidbody carRb; // Car의 Rigidbody 참조

    [Header("흔들림 크기 (각도)")]
    public float idleShake = 0.15f; // 정차 중(저속) 기본 흔들림

    public float roadShake = 0.5f; // 속도에 따라 추가되는 흔들림 크기

    public float shakeSpeed = 15f; // 흔들림 변화 속도

    Quaternion baseRot;

    // Start is called before the first frame update
    void Start()
    {
        baseRot = transform.localRotation; // 초기 회전값 저장
    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.linearVelocity.magnitude * 3.6f;

        // 정차 시 idleShake를 적용하고 속도에 따라 roadShake만큼 추가
        float shakeAmount = idleShake + (speedKmh / 100f) * roadShake;

        // Perlin 노이즈로 부드러운 무작위 값 생성 (-1~1)
        float t = Time.time * shakeSpeed;
        float nx = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f;

        // 초기 회전을 기준으로 차체를 살짝 회전
        transform.localRotation = baseRot * Quaternion.Euler(ny * shakeAmount, 0f, nx * shakeAmount);
    }
}
