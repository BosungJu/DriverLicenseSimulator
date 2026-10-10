using UnityEngine;

public class CarMoveInput
{
    public float Steering; //핸들
    public float Throttle; //엑셀
    public bool IsBraking; //브레이크

    public override string ToString()
    {
        return $"핸들: {Steering}, 엑셀: {Throttle}, 브레이크: {IsBraking}";
    }
}
