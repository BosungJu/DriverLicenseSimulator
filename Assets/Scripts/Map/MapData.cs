using UnityEngine;

public class MapData
{
    [SerializeField]
    private int index;
    public int Index => index;

    [SerializeField]
    private string name;
    public string Name => name;

    [SerializeField]
    private string layer;
    public string Layer => layer;

    [SerializeField]
    private float pos_x;
    public float PosX => pos_x;

    [SerializeField]
    private float pos_y;
    public float PosY => pos_y;

    [SerializeField]
    private float pos_z;
    public float PosZ => pos_z;

    [SerializeField]
    private float rotation;
    public float Rotation => rotation;

    [SerializeField]
    private int value;
    public int Value => value;

    [SerializeField]
    private float tilt;
    public float Tilt => tilt;

    [SerializeField]
    private float height;
    public float Height => height;

    [SerializeField]
    private float thickness;
    public float Thickness => thickness;

    [SerializeField]
    private string style;
    public string Style => style;

    [SerializeField]
    private float widthRatio;
    public float WidthRatio => widthRatio;

    [SerializeField]
    private float degree;
    public float Degree => degree;

    [SerializeField]
    private float length;
    public float Length => length;

    [SerializeField]
    private float end_x;
    public float EndX => end_x;

    [SerializeField]
    private float end_y;
    public float EndY => end_y;

    [SerializeField]
    private float end_z;
    public float EndZ => end_z;

    [SerializeField]
    private float delta_x;
    public float DeltaX => delta_x;

    [SerializeField]
    private float delta_y;
    public float DeltaY => delta_y;

    [SerializeField]
    private float delta_z;
    public float DeltaZ => delta_z;

    [SerializeField]
    private float start_x;
    public float StartX => start_x;

    [SerializeField]
    private float start_y;
    public float StartY => start_y;

    [SerializeField]
    private float start_z;
    public float StartZ => start_z;

    [SerializeField]
    private float pos_x1;
    public float PosX1 => pos_x1;

    [SerializeField]
    private float pos_y1;
    public float PosY1 => pos_y1;

    [SerializeField]
    private float pos_z1;
    public float PosZ1 => pos_z1;

    [SerializeField]
    private float width;
    public float Width => width;

    [SerializeField]
    private float minor_radius;
    public float MinorRadius => minor_radius;

    [SerializeField]
    private float minor_vector_x;
    public float MinorVectorX => minor_vector_x;

    [SerializeField]
    private float minor_vector_y;
    public float MinorVectorY => minor_vector_y;

    [SerializeField]
    private float minor_vector_z;
    public float MinorVectorZ => minor_vector_z;

    [SerializeField]
    private float area;
    public float Area => area;

    [SerializeField]
    private float radius_ratio;
    public float RadiusRatio => radius_ratio;

    [SerializeField]
    private float start_degree;
    public float StartDegree => start_degree;

    [SerializeField]
    private float major_radius;
    public float MajorRadius => major_radius;

    [SerializeField]
    private float major_vector_x;
    public float MajorVectorX => major_vector_x;

    [SerializeField]
    private float major_vector_y;
    public float MajorVectorY => major_vector_y;

    [SerializeField]
    private float major_vector_z;
    public float MajorVectorZ => major_vector_z;

    [SerializeField]
    private float centor_point_x;
    public float CentorPointX => centor_point_x;

    [SerializeField]
    private float centor_point_y;
    public float CentorPointY => centor_point_y;

    [SerializeField]
    private float centor_point_z;
    public float CentorPointZ => centor_point_z;

    [SerializeField]
    private string close;
    public string Close => close;

    [SerializeField]
    private float global_width;
    public float GlobalWidth => global_width;

    [SerializeField]
    private string associative;
    public string Associative => associative;

    [SerializeField]
    private float radius;
    public float Radius => radius;

    [SerializeField]
    private float total_angle;
    public float TotalAngle => total_angle;

    public MapData(
        int index,
        string name,
        string layer,
        float posX,
        float posY,
        float posZ,
        float rotation,
        int value,
        float tilt,
        float height,
        float thickness,
        string style,
        float widthRatio,
        float degree,
        float length,
        float endX,
        float endY,
        float endZ,
        float deltaX,
        float deltaY,
        float deltaZ,
        float startX,
        float startY,
        float startZ,
        float posX1,
        float posY1,
        float posZ1,
        float width,
        float minorRadius,
        float minorVectorX,
        float minorVectorY,
        float minorVectorZ,
        float area,
        float radiusRatio,
        float startDegree,
        float majorRadius,
        float majorVectorX,
        float majorVectorY,
        float majorVectorZ,
        float centorPointX,
        float centorPointY,
        float centorPointZ,
        string close,
        float globalWidth,
        string associative,
        float radius,
        float totalAngle)
    {
        this.index = index;
        this.name = name;
        this.layer = layer;
        this.pos_x = posX;
        this.pos_y = posY;
        this.pos_z = posZ;
        this.rotation = rotation;
        this.value = value;
        this.tilt = tilt;
        this.height = height;
        this.thickness = thickness;
        this.style = style;
        this.widthRatio = widthRatio;
        this.degree = degree;
        this.length = length;
        this.end_x = endX;
        this.end_y = endY;
        this.end_z = endZ;
        this.delta_x = deltaX;
        this.delta_y = deltaY;
        this.delta_z = deltaZ;
        this.start_x = startX;
        this.start_y = startY;
        this.start_z = startZ;
        this.pos_x1 = posX1;
        this.pos_y1 = posY1;
        this.pos_z1 = posZ1;
        this.width = width;
        this.minor_radius = minorRadius;
        this.minor_vector_x = minorVectorX;
        this.minor_vector_y = minorVectorY;
        this.minor_vector_z = minorVectorZ;
        this.area = area;
        this.radius_ratio = radiusRatio;
        this.start_degree = startDegree;
        this.major_radius = majorRadius;
        this.major_vector_x = majorVectorX;
        this.major_vector_y = majorVectorY;
        this.major_vector_z = majorVectorZ;
        this.centor_point_x = centorPointX;
        this.centor_point_y = centorPointY;
        this.centor_point_z = centorPointZ;
        this.close = close;
        this.global_width = globalWidth;
        this.associative = associative;
        this.radius = radius;
        this.total_angle = totalAngle;
    }
}
