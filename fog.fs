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

#define bottom 0   // 云层底部
#define top 5      // 云层顶部
#define width 8     // 云层 xz 坐标范围 [-width, width]

#define baseBright  vec3(1.26,1.25,1.29)    // 基础颜色 -- 亮部
#define baseDark    vec3(0.31,0.31,0.32)    // 基础颜色 -- 暗部
#define lightBright vec3(1.05, 1.17, 1.29)  // 光照颜色 -- 亮部
#define lightDark   vec3(0.7,0.75,0.8)      // 光照颜色 -- 暗部

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
// 高度衰减
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
          float a = 1.35;//阈值
    return max(n - a, 0.0) * (1.0 / (1.0 - a)*weight);
}

// 获取体积云颜色
vec4 getCloud(vec3 worldPos, vec3 cameraPos) 
{
    vec3 direction = normalize(worldPos - cameraPos);   // 视线射线方向
    vec3 step = direction * 0.25;   // 步长
    vec4 colorSum = vec4(0);        // 积累的颜色
    vec3 point = cameraPos;         // 从相机出发开始测试

    // 如果相机在云层下，将测试起始点移动到云层底部 bottom
    if(point.y<bottom) {
        point += direction * (abs(bottom - cameraPos.y) / abs(direction.y));
    }
    // 如果相机在云层上，将测试起始点移动到云层顶部 top
    if(top<point.y) {
        point += direction * (abs(cameraPos.y - top) / abs(direction.y));
    }

    // 如果目标像素遮挡了云层则放弃测试
    //float len1 = length(point - cameraPos);     // 云层到眼距离
    //float len2 = length(worldPos - cameraPos);  // 目标像素到眼距离
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

        // 转屏幕坐标
        vec4 screenPos = projection * view * vec4(point, 1.0);
        screenPos /= screenPos.w;
        screenPos.xyz = screenPos.xyz * 0.5 + 0.5;
        
        // 深度采样
        float sampleDepth = texture2D(depthMap, screenPos.xy).r;    // 采样深度
        float testDepth = screenPos.z;  // 测试深度
        
        // 深度线性化
        //sampleDepth = linearizeDepth(sampleDepth);
        //testDepth = linearizeDepth(testDepth);
        
        // hit 则停止
        if(sampleDepth<testDepth) {
            break;
        }
        
        // 采样
        float density = getCloudNoise(point);                // 当前点云密度
        vec3 L = normalize(lightPos - point);                       // 光源方向
        float lightDensity = getCloudNoise(point + L);       // 向光源方向采样一次 获取密度
        float delta = clamp(density - lightDensity, 0.0, 1.0);      // 两次采样密度差

        // 控制透明度
        density *= 0.5;

        // 颜色计算
        vec3 base = mix(baseBright, baseDark, density) * density;   // 基础颜色
        vec3 light = mix(lightDark, lightBright, delta);            // 光照对颜色影响

        // 混合
        vec4 color = vec4(base*light, density);                     // 当前点的最终颜色
        colorSum = color * (1.0 - colorSum.a) + colorSum;           // 与累积的颜色混合
    }

    return colorSum;
}

void main()
{
    vec4 cloud = getCloud(PixeltoWorld(), cameraPos); // 云颜色
    vec3 col = texture(screenTexture, TexCoords).rgb;
    FragColor.rgb = col.rgb*(1.0 - cloud.a) + cloud.rgb;    // 混色
} 