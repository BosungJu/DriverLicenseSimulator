using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CabShake : MonoBehaviour
{
    public Rigidbody carRb; //Car�� Rigidbody ����

    [Header("��鸲 ũ�� (����)")]
    public float idleShake = 0.15f; //��ȸ��(����) �� ��鸲

    public float roadShake = 0.5f; //���ӿ��� �߰��Ǵ� ��� ����

    public float shakeSpeed = 15f; //��鸲 ������

    Quaternion baseRot;

    // Start is called before the first frame update
    void Start()
    {
        baseRot = transform.localRotation; //���� ���� ��� (0,0,0)
    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.linearVelocity.magnitude * 3.6f;

        //���� �� idleShake, �������� roadShake��ŭ �� ��鸮��
        float shakeAmount = idleShake + (speedKmh / 100f) * roadShake;

        //Perlin ������ = �ε巯�� ���� �� (-1 ~ 1)
        float t = Time.time * shakeSpeed;
        float nx = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f;

        //���� ���⿡�� ���� ��¦ ����
        transform.localRotation = baseRot * Quaternion.Euler(ny * shakeAmount, 0f, nx * shakeAmount);
    }
}
