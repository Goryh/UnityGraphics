#ifndef UNITY_POSTFX_DISK_KERNELS
#define UNITY_POSTFX_DISK_KERNELS

#if !defined(KERNEL_SMALL) && !defined(KERNEL_MEDIUM) && \
    !defined(KERNEL_LARGE) && !defined(KERNEL_VERYLARGE)

static const int kSampleCount = 1;
static const float2 kDiskKernel[1] = { float2(0, 0) };

#endif

#if defined(KERNEL_SMALL)

// rings = 2
// points per ring = 5
static const int kSampleCount = 16;
static const float2 kDiskKernel[kSampleCount] = {
    float2(0,0),
    float2(0.54545456,0),
    float2(0.16855472,0.5187581),
    float2(-0.44128203,0.3206101),
    float2(-0.44128197,-0.3206102),
    float2(0.1685548,-0.5187581),
    float2(1,0),
    float2(0.809017,0.58778524),
    float2(0.30901697,0.95105654),
    float2(-0.30901703,0.9510565),
    float2(-0.80901706,0.5877852),
    float2(-1,0),
    float2(-0.80901694,-0.58778536),
    float2(-0.30901664,-0.9510566),
    float2(0.30901712,-0.9510565),
    float2(0.80901694,-0.5877853),
};

#endif

#if defined(KERNEL_MEDIUM)

// rings = 3
// points per ring = 7
static const int kSampleCount = 22;
static const float2 kDiskKernel[kSampleCount] = {
    float2(0,0),
    float2(0.53333336,0),
    float2(0.3325279,0.4169768),
    float2(-0.11867785,0.5199616),
    float2(-0.48051673,0.2314047),
    float2(-0.48051673,-0.23140468),
    float2(-0.11867763,-0.51996166),
    float2(0.33252785,-0.4169769),
    float2(1,0),
    float2(0.90096885,0.43388376),
    float2(0.6234898,0.7818315),
    float2(0.22252098,0.9749279),
    float2(-0.22252095,0.9749279),
    float2(-0.62349,0.7818314),
    float2(-0.90096885,0.43388382),
    float2(-1,0),
    float2(-0.90096885,-0.43388376),
    float2(-0.6234896,-0.7818316),
    float2(-0.22252055,-0.974928),
    float2(0.2225215,-0.9749278),
    float2(0.6234897,-0.7818316),
    float2(0.90096885,-0.43388376),
};

#endif

#if defined(KERNEL_LARGE)

// rings = 4
// points per ring = 7
static const int kSampleCount = 43;
static const float2 kDiskKernel[kSampleCount] = {
    float2(0,0),
    float2(0.36363637,0),
    float2(0.22672357,0.28430238),
    float2(-0.08091671,0.35451925),
    float2(-0.32762504,0.15777594),
    float2(-0.32762504,-0.15777591),
    float2(-0.08091656,-0.35451928),
    float2(0.22672352,-0.2843024),
    float2(0.6818182,0),
    float2(0.614297,0.29582983),
    float2(0.42510667,0.5330669),
    float2(0.15171885,0.6647236),
    float2(-0.15171883,0.6647236),
    float2(-0.4251068,0.53306687),
    float2(-0.614297,0.29582986),
    float2(-0.6818182,0),
    float2(-0.614297,-0.29582983),
    float2(-0.42510656,-0.53306705),
    float2(-0.15171856,-0.66472363),
    float2(0.1517192,-0.6647235),
    float2(0.4251066,-0.53306705),
    float2(0.614297,-0.29582983),
    float2(1,0),
    float2(0.9555728,0.2947552),
    float2(0.82623875,0.5633201),
    float2(0.6234898,0.7818315),
    float2(0.36534098,0.93087375),
    float2(0.07473,0.9972038),
    float2(-0.22252095,0.9749279),
    float2(-0.50000006,0.8660254),
    float2(-0.73305196,0.6801727),
    float2(-0.90096885,0.43388382),
    float2(-0.98883086,0.14904208),
    float2(-0.9888308,-0.14904249),
    float2(-0.90096885,-0.43388376),
    float2(-0.73305184,-0.6801728),
    float2(-0.4999999,-0.86602545),
    float2(-0.222521,-0.9749279),
    float2(0.07473029,-0.99720377),
    float2(0.36534148,-0.9308736),
    float2(0.6234897,-0.7818316),
    float2(0.8262388,-0.56332),
    float2(0.9555729,-0.29475483),
};

#endif

#if defined(KERNEL_VERYLARGE)

// rings = 5
// points per ring = 7
static const int kSampleCount = 71;
static const float2 kDiskKernel[kSampleCount] = {
    float2(0,0),
    float2(0.2758621,0),
    float2(0.1719972,0.21567768),
    float2(-0.061385095,0.26894566),
    float2(-0.24854316,0.1196921),
    float2(-0.24854316,-0.11969208),
    float2(-0.061384983,-0.2689457),
    float2(0.17199717,-0.21567771),
    float2(0.51724136,0),
    float2(0.46601835,0.22442262),
    float2(0.32249472,0.40439558),
    float2(0.11509705,0.50427306),
    float2(-0.11509704,0.50427306),
    float2(-0.3224948,0.40439552),
    float2(-0.46601835,0.22442265),
    float2(-0.51724136,0),
    float2(-0.46601835,-0.22442262),
    float2(-0.32249463,-0.40439564),
    float2(-0.11509683,-0.5042731),
    float2(0.11509732,-0.504273),
    float2(0.32249466,-0.40439564),
    float2(0.46601835,-0.22442262),
    float2(0.7586207,0),
    float2(0.7249173,0.22360738),
    float2(0.6268018,0.4273463),
    float2(0.47299224,0.59311354),
    float2(0.27715522,0.7061801),
    float2(0.056691725,0.75649947),
    float2(-0.168809,0.7396005),
    float2(-0.3793104,0.65698475),
    float2(-0.55610836,0.51599306),
    float2(-0.6834936,0.32915324),
    float2(-0.7501475,0.113066405),
    float2(-0.7501475,-0.11306671),
    float2(-0.6834936,-0.32915318),
    float2(-0.5561083,-0.5159932),
    float2(-0.37931028,-0.6569848),
    float2(-0.16880904,-0.7396005),
    float2(0.056691945,-0.7564994),
    float2(0.2771556,-0.7061799),
    float2(0.47299215,-0.59311366),
    float2(0.62680185,-0.4273462),
    float2(0.72491735,-0.22360711),
    float2(1,0),
    float2(0.9749279,0.22252093),
    float2(0.90096885,0.43388376),
    float2(0.7818315,0.6234898),
    float2(0.6234898,0.7818315),
    float2(0.43388364,0.9009689),
    float2(0.22252098,0.9749279),
    float2(0,1),
    float2(-0.22252095,0.9749279),
    float2(-0.43388385,0.90096885),
    float2(-0.62349,0.7818314),
    float2(-0.7818317,0.62348956),
    float2(-0.90096885,0.43388382),
    float2(-0.9749279,0.22252093),
    float2(-1,0),
    float2(-0.9749279,-0.22252087),
    float2(-0.90096885,-0.43388376),
    float2(-0.7818314,-0.6234899),
    float2(-0.6234896,-0.7818316),
    float2(-0.43388346,-0.900969),
    float2(-0.22252055,-0.974928),
    float2(0,-1),
    float2(0.2225215,-0.9749278),
    float2(0.4338835,-0.90096897),
    float2(0.6234897,-0.7818316),
    float2(0.78183144,-0.62348986),
    float2(0.90096885,-0.43388376),
    float2(0.9749279,-0.22252086),
};

#endif


static const int kDiskAllKernelSizes[7] = { 1, 8, 22, 43, 71, 106, 148 };
static const float2 kDiskAllKernels[148] = {
float2(0, 0),
// ring 1 index=1
float2(0.186046511627907, 0),
float2(0.115998102671392, 0.145457019994052),
float2(-0.0413992435267562, 0.181381937150107),
float2(-0.16762211495859, 0.0807225561148946),
float2(-0.16762211495859, -0.0807225561148945),
float2(-0.0413992435267562, -0.181381937150107),
float2(0.115998102671392, -0.145457019994052),
// ring 2 index=8
float2(0.348837209302326, 0),
float2(0.314291465547356, 0.151354792715427),
float2(0.217496442508861, 0.272731912488848),
float2(0.0776235816126678, 0.34009113215645),
float2(-0.0776235816126678, 0.34009113215645),
float2(-0.217496442508861, 0.272731912488848),
float2(-0.314291465547355, 0.151354792715427),
float2(-0.348837209302326, 4.27202371795588E-17),
float2(-0.314291465547356, -0.151354792715427),
float2(-0.217496442508861, -0.272731912488848),
float2(-0.0776235816126679, -0.34009113215645),
float2(0.0776235816126674, -0.34009113215645),
float2(0.21749644250886, -0.272731912488848),
float2(0.314291465547356, -0.151354792715427),
// ring 3 index=22
float2(0.511627906976744, 0),
float2(0.488897714588258, 0.150804972954416),
float2(0.422726814766323, 0.288210262265109),
float2(0.318994782346329, 0.400006804983643),
float2(0.186918663629318, 0.47626098767843),
float2(0.0382340013697985, 0.510197291581069),
float2(-0.113847919698579, 0.498800327162793),
float2(-0.255813953488372, 0.443082764726922),
float2(-0.375049794889679, 0.347995354208377),
float2(-0.460960816136121, 0.22198702931596),
float2(-0.505913445975647, 0.0762541826947871),
float2(-0.505913445975647, -0.0762541826947867),
float2(-0.460960816136121, -0.22198702931596),
float2(-0.375049794889679, -0.347995354208377),
float2(-0.255813953488372, -0.443082764726922),
float2(-0.11384791969858, -0.498800327162793),
float2(0.0382340013697985, -0.510197291581069),
float2(0.186918663629319, -0.47626098767843),
float2(0.318994782346329, -0.400006804983643),
float2(0.422726814766323, -0.288210262265109),
float2(0.488897714588258, -0.150804972954416),
// ring 4 index=43
float2(0.674418604651163, 0),
float2(0.657509522169137, 0.150072257784491),
float2(0.607630166724887, 0.292619265916493),
float2(0.527281697478439, 0.420493122183797),
float2(0.420493122183797, 0.527281697478439),
float2(0.292619265916493, 0.607630166724887),
float2(0.150072257784491, 0.657509522169137),
float2(4.12962292735735E-17, 0.674418604651163),
float2(-0.150072257784491, 0.657509522169137),
float2(-0.292619265916493, 0.607630166724887),
float2(-0.420493122183797, 0.527281697478439),
float2(-0.527281697478438, 0.420493122183797),
float2(-0.607630166724887, 0.292619265916493),
float2(-0.657509522169137, 0.150072257784491),
float2(-0.674418604651163, 8.25924585471471E-17),
float2(-0.657509522169137, -0.150072257784491),
float2(-0.607630166724887, -0.292619265916493),
float2(-0.527281697478439, -0.420493122183797),
float2(-0.420493122183797, -0.527281697478439),
float2(-0.292619265916493, -0.607630166724887),
float2(-0.150072257784491, -0.657509522169137),
float2(-1.23888687820721E-16, -0.674418604651163),
float2(0.15007225778449, -0.657509522169137),
float2(0.292619265916493, -0.607630166724887),
float2(0.420493122183797, -0.527281697478439),
float2(0.527281697478439, -0.420493122183797),
float2(0.607630166724887, -0.292619265916492),
float2(0.657509522169137, -0.150072257784491),
// ring 5 index=71
float2(0.837209302325581, 0),
float2(0.823755004408155, 0.149489493319789),
float2(0.783824542861175, 0.294174271323915),
float2(0.718701315573655, 0.429404046200294),
float2(0.630478436654186, 0.550832421716969),
float2(0.521991462021265, 0.654556589973234),
float2(0.396727252302976, 0.737242770856804),
float2(0.258711902267398, 0.796233362479663),
float2(0.112381338824084, 0.829632358689434),
float2(-0.0375612533167101, 0.836366288267147),
float2(-0.186296595870403, 0.81621871717548),
float2(-0.329044212547471, 0.769837204926796),
float2(-0.461216077494783, 0.698712491487602),
float2(-0.578564078221561, 0.605130583669444),
float2(-0.677316553430188, 0.492099280989047),
float2(-0.754299517313653, 0.363251502517026),
float2(-0.807038674070947, 0.222728521869775),
float2(-0.833838943809968, 0.0750468632679909),
float2(-0.833838943809968, -0.0750468632679907),
float2(-0.807038674070947, -0.222728521869774),
float2(-0.754299517313653, -0.363251502517025),
float2(-0.677316553430189, -0.492099280989047),
float2(-0.578564078221562, -0.605130583669444),
float2(-0.461216077494784, -0.698712491487602),
float2(-0.329044212547471, -0.769837204926796),
float2(-0.186296595870403, -0.81621871717548),
float2(-0.0375612533167103, -0.836366288267147),
float2(0.112381338824084, -0.829632358689434),
float2(0.258711902267398, -0.796233362479664),
float2(0.396727252302976, -0.737242770856804),
float2(0.521991462021265, -0.654556589973234),
float2(0.630478436654186, -0.550832421716969),
float2(0.718701315573655, -0.429404046200294),
float2(0.783824542861175, -0.294174271323915),
float2(0.823755004408155, -0.149489493319789),
// ring 6 index=106
float2(1, 0),
float2(0.988830826225129, 0.149042266176174),
float2(0.955572805786141, 0.294755174410904),
float2(0.900968867902419, 0.433883739117558),
float2(0.826238774315995, 0.563320058063622),
float2(0.733051871829826, 0.680172737770919),
float2(0.623489801858734, 0.78183148246803),
float2(0.5, 0.866025403784439),
float2(0.365341024366395, 0.930873748644204),
float2(0.222520933956314, 0.974927912181824),
float2(0.0747300935864244, 0.99720379718118),
float2(-0.074730093586424, 0.99720379718118),
float2(-0.222520933956314, 0.974927912181824),
float2(-0.365341024366395, 0.930873748644204),
float2(-0.5, 0.866025403784439),
float2(-0.623489801858733, 0.78183148246803),
float2(-0.733051871829826, 0.680172737770919),
float2(-0.826238774315995, 0.563320058063622),
float2(-0.900968867902419, 0.433883739117558),
float2(-0.955572805786141, 0.294755174410905),
float2(-0.988830826225129, 0.149042266176175),
float2(-1, 1.22464679914735E-16),
float2(-0.988830826225129, -0.149042266176174),
float2(-0.955572805786141, -0.294755174410904),
float2(-0.900968867902419, -0.433883739117558),
float2(-0.826238774315995, -0.563320058063622),
float2(-0.733051871829826, -0.680172737770919),
float2(-0.623489801858734, -0.78183148246803),
float2(-0.5, -0.866025403784438),
float2(-0.365341024366395, -0.930873748644204),
float2(-0.222520933956315, -0.974927912181824),
float2(-0.0747300935864247, -0.99720379718118),
float2(0.0747300935864244, -0.99720379718118),
float2(0.222520933956314, -0.974927912181824),
float2(0.365341024366395, -0.930873748644204),
float2(0.499999999999999, -0.866025403784439),
float2(0.623489801858733, -0.78183148246803),
float2(0.733051871829827, -0.680172737770919),
float2(0.826238774315994, -0.563320058063623),
float2(0.900968867902419, -0.433883739117558),
float2(0.955572805786141, -0.294755174410905),
float2(0.988830826225128, -0.149042266176175),
};

#endif // UNITY_POSTFX_DISK_KERNELS
