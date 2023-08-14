#version 330 core
out vec4 FragColor;

in vec2 TexCoords;

uniform sampler2D screenTexture;
uniform sampler2D Fognoise;
uniform sampler2D depthMap;
uniform mat4 view;
uniform mat4 projection;
uniform vec3 cameraPos;
uniform float iTime;
uniform float near_plane;
uniform float far_plane;

#define bottom 0   // �Ʋ�ײ�
#define top 5      // �Ʋ㶥��
#define width 8     // �Ʋ� xz ���귶Χ [-width, width]

#define baseBright  vec3(1.26,1.25,1.29)    // ������ɫ -- ����
#define baseDark    vec3(0.31,0.31,0.32)    // ������ɫ -- ����
#define lightBright vec3(1.05, 1.17, 1.29)  // ������ɫ -- ����
#define lightDark   vec3(0.7,0.75,0.8)      // ������ɫ -- ����

vec3 lightPos = vec3(0.0f,8.0f,0.0f);

vec3 PixeltoWorld()
{
    vec4 ndc = vec4(0.0);
    ndc.x = (gl_FragCoord.x/800.0) * 2.0 - 1.0;
    ndc.y = (gl_FragCoord.y/600.0) * 2.0 - 1.0;
    ndc.z = gl_FragCoord.z * 2.0 - 1.0;
    ndc.w = 1.0 ;
    
    vec4 worldpos = vec4(0.0);
    worldpos = inverse(view) * inverse(projection) * ndc;
    worldpos.xyz /= worldpos.w;
    return worldpos.xyz;
}

float linearizeDepth(float depth) {
    return (2.0 * near_plane) / (far_plane + near_plane - depth * (far_plane - near_plane));
}

float noise(vec3 x)
{
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = smoothstep(0.0, 1.0, f);
     
    vec2 uv = (p.xy+vec2(37.0, 17.0)*p.z) + f.xy;
    float v1 = texture2D( Fognoise, (uv)/256.0, -100.0 ).x;
    float v2 = texture2D( Fognoise, (uv + vec2(37.0, 17.0))/256.0, -100.0 ).x;
    return mix(v1, v2, f.z);
}
 
float getCloudNoise(vec3 worldPos) {
// �߶�˥��
float mid = (bottom + top) / 2.0;
float h = top - bottom;
float weight = 1.0 - 2.0 * abs(mid - worldPos.y) / h;
weight = pow(weight, 0.5);

    vec3 coord = worldPos;
    coord.x +=iTime;
    coord *= 0.1;
    float n  = noise(coord) * 0.5;   coord *= 2.02;
          n += noise(coord) * 0.25;  coord *= 2.41;
          n += noise(coord) * 0.125; coord *= 2.80;
          n += noise(coord) * 0.0625;
          //coord *= 3.03; n+=noise(coord) * 0.03125;
          float a = 1.35;//��ֵ
    return max(n - a, 0.0) * (1.0 / (1.0 - a)*weight);
}

// ��ȡ�������ɫ
vec4 getCloud(vec3 worldPos, vec3 cameraPos) 
{
    vec3 direction = normalize(worldPos - cameraPos);   // �������߷���
    vec3 step = direction * 0.25;   // ����
    vec4 colorSum = vec4(0);        // ���۵���ɫ
    vec3 point = cameraPos;         // �����������ʼ����

    // ���������Ʋ��£���������ʼ���ƶ����Ʋ�ײ� bottom
    if(point.y<bottom) {
        point += direction * (abs(bottom - cameraPos.y) / abs(direction.y));
    }
    // ���������Ʋ��ϣ���������ʼ���ƶ����Ʋ㶥�� top
    if(top<point.y) {
        point += direction * (abs(cameraPos.y - top) / abs(direction.y));
    }

    // ���Ŀ�������ڵ����Ʋ����������
    //float len1 = length(point - cameraPos);     // �Ʋ㵽�۾���
    //float len2 = length(worldPos - cameraPos);  // Ŀ�����ص��۾���
    //if(len2<len1) {
    //    return vec4(0);
    //}

    // ray marching
    for(int i=0; i<1000; i++) 
    {
        point += step * (1.0 + i*0.05);
        if(bottom>point.y || point.y>top || -width>point.x || point.x>width || -width>point.z || point.z>width)
        {
            break;
        }

        // ת��Ļ����
        vec4 screenPos = projection * view * vec4(point, 1.0);
        screenPos /= screenPos.w;
        screenPos.xyz = screenPos.xyz * 0.5 + 0.5;
        
        // ��Ȳ���
        float sampleDepth = texture2D(depthMap, screenPos.xy).r;    // �������
        float testDepth = screenPos.z;  // �������
        
        // ������Ի�
        //sampleDepth = linearizeDepth(sampleDepth);
        //testDepth = linearizeDepth(testDepth);
        
        // hit ��ֹͣ
        if(sampleDepth<testDepth) {
            break;
        }
        
        // ����
        float density = getCloudNoise(point);                // ��ǰ�����ܶ�
        vec3 L = normalize(lightPos - point);                       // ��Դ����
        float lightDensity = getCloudNoise(point + L);       // ���Դ�������һ�� ��ȡ�ܶ�
        float delta = clamp(density - lightDensity, 0.0, 1.0);      // ���β����ܶȲ�

        // ����͸����
        density *= 0.5;

        // ��ɫ����
        vec3 base = mix(baseBright, baseDark, density) * density;   // ������ɫ
        vec3 light = mix(lightDark, lightBright, delta);            // ���ն���ɫӰ��

        // ���
        vec4 color = vec4(base*light, density);                     // ��ǰ���������ɫ
        colorSum = color * (1.0 - colorSum.a) + colorSum;           // ���ۻ�����ɫ���
    }

    return colorSum;
}

void main()
{
    vec4 cloud = getCloud(PixeltoWorld(), cameraPos); // ����ɫ
    vec3 col = texture(screenTexture, TexCoords).rgb;
    FragColor.rgb = col.rgb*(1.0 - cloud.a) + cloud.rgb;    // ��ɫ
} 