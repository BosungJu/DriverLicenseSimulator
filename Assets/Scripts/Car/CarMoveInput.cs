using UnityEngine;

public class CarMoveInput
{
    public float Steering; //핸들
    public float Throttle; //엑셀 (0 ~ 1)
    public float Brake; //브레이크 (0 ~ 1)

    public bool IsBraking => Brake > 0f;

    public override string ToString()
    {
        return $"핸들: {Steering}, 엑셀: {Throttle}, 브레이크: {Brake}";
    }
}
