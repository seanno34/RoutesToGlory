{\rtf1\ansi\ansicpg1252\cocoartf2822
\cocoatextscaling0\cocoaplatform0{\fonttbl\f0\fswiss\fcharset0 Helvetica;}
{\colortbl;\red255\green255\blue255;}
{\*\expandedcolortbl;;}
\margl1440\margr1440\vieww11520\viewh8400\viewkind0
\pard\tx720\tx1440\tx2160\tx2880\tx3600\tx4320\tx5040\tx5760\tx6480\tx7200\tx7920\tx8640\pardirnatural\partightenfactor0

\f0\fs24 \cf0 # Goal\
\
Transform the night sky into a memorable alien skyline by adding two massive celestial bodies that permanently decorate the horizon.\
\
The planets should feel physically distant and enormous, not like nearby floating objects.\
\
They are environmental art only.\
\
No gameplay interaction.\
\
---\
\
# Composition\
\
The player should never see both planets close together.\
\
They should balance the skyline.\
\
Planet A (Primary)\
------------------\
\
This is the hero planet.\
\
Use the large GREEN ringed planet.\
\
Place it approximately 2\'965 degrees above the horizon.\
\
It should appear to be rising over the horizon similar to a full moon on Earth.\
\
Only about 55\'9670% of the planet should be visible above the horizon line.\
\
The rings should be angled approximately 20\'9635 degrees.\
\
The rings should intersect the horizon to help sell the immense scale.\
\
This should be the dominant feature of the sky.\
\
Apparent size:\
\
Approximately 8\'9612x the angular diameter of Earth's moon.\
\
The player should immediately notice it from anywhere on the map.\
\
---\
\
Planet B (Secondary)\
\
Use the blue ringless planet.\
\
Place it roughly 170\'96180 degrees opposite Planet A.\
\
It should sit just above the opposite horizon.\
\
Smaller than Planet A.\
\
Approximately 35\'9650% of Planet A's apparent diameter.\
\
No rings.\
\
Its purpose is visual balance.\
\
---\
\
Lighting\
\
Both planets must use the same directional light as the world.\
\
The illuminated side should always face the primary star (sun).\
\
Do not fake random lighting.\
\
---\
\
Atmospheric Effects\
\
Fade both planets slightly into atmospheric haze near the horizon.\
\
Reduce contrast near the horizon.\
\
Add a faint atmospheric bloom.\
\
Do not make the edges razor sharp.\
\
---\
\
Depth\
\
These planets should appear millions of kilometers away.\
\
Do NOT:\
\
\'95 move with the camera\
\
\'95 exhibit parallax\
\
\'95 rotate with player movement\
\
\'95 feel attached to the skybox\
\
If implemented as skybox objects, they should remain effectively fixed relative to the celestial sphere.\
\
---\
\
Performance\
\
These are static environmental objects.\
\
No physics.\
\
No colliders.\
\
No shadows.\
\
No reflections.\
\
No particle systems.\
\
---\
\
Implementation\
\
Preferred approach:\
\
Create a "CelestialBodies" parent object.\
\
CelestialBodies\
    RingedPlanet\
    RinglessPlanet\
\
Each object should expose:\
\
- Scale\
- Horizon elevation\
- Azimuth\
- Rotation\
- Brightness\
- Tint\
- Ring angle\
\
through serialized fields.\
\
Avoid hardcoded transforms.\
\
---\
\
Art Direction\
\
The player should subconsciously feel they are exploring an inhabited alien solar system.\
\
The sky should evoke:\
\
No Man's Sky\
\
Elite Dangerous\
\
Starfield\
\
Mass Effect\
\
without copying any of those games.\
\
The planets should inspire curiosity every time the player looks toward the horizon.\
\
The ringed planet is the iconic visual signature of the world.}