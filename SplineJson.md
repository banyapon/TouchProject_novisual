# SplineJson ใน Unity Editor

1. เลือก **Tools → KMITL Services → Create Spline JSON Junction** หรือเพิ่ม component **Spline Json** บน GameObject ใหม่
2. ช่อง **Junction Json** ใช้ `Assets/Resources/data/junction.json` โดยค่าเริ่มต้น
3. กด **Refresh** เพื่อสร้าง 8 road splines และ 2 ช่วงเชื่อมทางตรง
4. ปรับ **Road Width**, **Curb Width**, **Curb Height** และ Material แล้วกด **Extrude**

Extrude สร้างลูก `Generated Road Mesh/Road Surface` และ `Generated Road Mesh/Raised Curbs` พร้อม MeshFilter, MeshRenderer และ MeshCollider เมื่อเปิด Add Mesh Colliders พื้นกลางแยกต่อเนื่องและไม่สร้างขอบยกสูงขวางช่องทางที่เชื่อมกัน

Mesh และ Material ที่สร้างใหม่บันทึกใน `Assets/Generated/SplineJson/<ชื่อวัตถุ>/` กดซ้ำจะอัปเดต asset เดิมของวัตถุนั้น เมื่อ Duplicate วัตถุแล้ว Extrude ระบบแยก mesh asset ให้วัตถุใหม่ ส่วน material ที่ผู้ใช้กำหนดจะใช้ร่วมตามที่กำหนดไว้

Refresh ซ่อน mesh รุ่นก่อนจนกด Extrude ใหม่ เพื่อไม่แสดง geometry ที่ไม่ตรง JSON ข้อมูลไม่ถูกต้องจะแสดงข้อความใน Inspector และคง spline เดิมไว้ ทั้ง Refresh และ Extrude รองรับ Undo

## พิกัดและทิศทาง

- JSON ใช้ `[x,y]` โดย y ชี้ลงภาพ แปลงเป็น Unity local `(X, Y=Surface Height, Z)` โดยกลับแกนภาพ y เป็น Z
- จุดเริ่มถนน ID 1 ใน JSON คือ `[0,0]` และศูนย์แยกคือ `[0,-200]` (แกน y ยังชี้ลงภาพ)
- ค่าเริ่มต้น JSON Origin = `(0,-200)` และ Units Per Json Unit = `0.1` ทำให้ศูนย์แยกอยู่ที่ตำแหน่ง GameObject และคงตำแหน่งถนน/mesh ใน Scene เดิม จุดเริ่ม ID 1 จึงเป็น local `(0,0.02,-20)` เมื่อ Surface Height = `0.02`
- S คือปลาย 0, E คือปลาย 1 ตามลำดับ points
- เข้า `direction=0` วิ่ง S→E; เข้า `direction=1` วิ่ง E→S บน spline/ID เดิม
- เส้นโค้งที่มี controlPoint แปลง quadratic Bezier เป็น cubic Bezier ของ Unity แบบตรงกัน ไม่สร้างขากลับซ้ำ
- `mainRoad` อยู่ใน SplineContainer ลูกแยกจากถนนหลัก จึงไม่เพิ่ม road ID แต่มีพื้น mesh ต่อผ่านกลางแยก

เปิด Gizmos ใน Scene View แล้วเลือกวัตถุเพื่อดู spline และลูกศร: เหลือง S→E, เขียว E→S เลือก **Inspect Connections** ใน Inspector เพื่อดูปลาย S/E และลูกศรเข้าสู่ถนนปลายทางตาม direction สีฟ้าแสดงช่วงเชื่อมทางตรง เปิดหัวข้อ **Road connections** เพื่อดูรายการปลายเชื่อมและทางออกเริ่มต้น

สคริปต์อื่นเรียก `FindRoad(id)` เพื่ออ่าน nodeS/nodeE และ `TryGetTravelPose(id, enterNode, progress, out position, out forward)` เพื่ออ่านตำแหน่งและทิศใน world space ได้ progress อยู่ในช่วง 0–1 ของถนนปัจจุบัน การข้ามถนนและเลือกทางยังเป็นหน้าที่ของ movement controller

## SplineJsonMovement

ฉาก `Assets/Demo/SplineJSON.unity` ใช้ `splinemovement` บน Player โดยตรง กำหนดช่อง **Spline Json** เป็น SplineJson ของฉาก และปล่อย **Road Network** ว่าง เริ่มที่ถนน 1 ปลาย S หันเข้าทางแยก มี Movement เพียงตัวเดียว และ TouchpadManager ส่งอินพุตให้ตัวนี้ผ่าน API เดิม

`splinemovement` รองรับทั้ง RoadNetworkSplineCreator และ SplineJson หากกำหนดทั้งสองช่องจะใช้ Spline Json ก่อน ฉากเดิมที่กำหนด Road Network ยังทำงานตามเดิม ส่วน `SplineJsonMovement` คงไว้สำหรับฉากที่เคยใช้งานและสืบทอดการทำงานทั้งหมดจากตัวหลัก

ทั้งสองแบบใช้ระยะลากและ dead zone เดิม ปัดซ้าย/ขวาเลือกทางครั้งเดียวต่อ gesture, ไฮไลต์ทางเลือก, Slider, หมุนเฉพาะกล้องด้วยสองนิ้ว, หันตาม tangent และถอยหลังตาม history เดิม การปัดเลือกทางยังไม่เปลี่ยน history จนข้ามเข้าเส้นใหม่จริง

ตัวอ่านเส้นทาง `SplineJsonMovementRoute` ใช้ ID/ปลาย S/E จาก JSON และวัดระยะตามความยาวจริงใน world space ขาไป–กลับของเส้นโค้งใช้ ID เดียวกัน ส่วน `mainRoad` ใช้ระยะเดินจริงผ่านกลางแยก ไม่กระโดดไปอีกปลายและไม่เพิ่ม road ID ถ้าถอยกลางช่วงเชื่อมจะกลับตามระยะเดิมได้ เมื่อถึงขอบนอกที่ไม่มีถนนต่อจะหยุด

ขณะ Play Mode ดู **Current Route** ใน Inspector เพื่ออ่าน ID ปัจจุบัน ระยะจาก S ทิศทาง เลนที่เลือก และ history หากกำลังผ่านช่วงตรงกลางแยก จะแสดง **Straight Connection** เพิ่ม โดยยังรายงาน ID ถนนที่ออกมาจนกว่าจะเข้าถนนถัดไป

ทดสอบการเคลื่อนที่และฉากจริงในโปรเจกต์ชั่วคราวแยก โดยจำลอง packet อินพุตโดยไม่ต้องใช้ฮาร์ดแวร์ touchpad:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/Run-SplineJsonMovementValidation.ps1
```

## JSON แบบย่อและข้อความบน GUI

ชื่อฟิลด์การเชื่อมใน JSON คือ `direction` แทน `enterNode` เช่น `{ "roadNo": 5, "direction": 0 }` หมายถึงเข้าเส้น 5 ที่ S และวิ่งไป E ส่วน `direction: 1` เข้า E และวิ่งไป S เป็นทิศของถนนปลายทาง ไม่ใช่ถนนที่กำลังออก

`mainRoad` แทนชื่อเดิม `straightConnections` ใช้เก็บช่วงเชื่อมตรง เช่น 1–2 และ 3–4 โดยไม่เพิ่ม ID ส่วน `fromNode` และ `toNode` ยังคงบอกปลายเชื่อม S=0/E=1 ตัวอ่านยังรองรับชื่อเก่าในไฟล์เดิมด้วย

GUI ใช้คำว่า `Road` ส่วนเส้นเชื่อมแสดงคู่ถนนตามทิศ เช่น `Road 5(1-3)` เมื่อ `Direction 0` (S→E) และ `Road 5(3-1)` เมื่อ `Direction 1` (E→S) โดยอ่านหมายเลขจาก nodeS/nodeE จริง ไม่ต้องเก็บ label คู่ถนนซ้ำใน JSON เส้น 6–8 ใช้กติกาเดียวกัน Direction หมายถึงทิศที่หันตามเส้น; การถอยหลังด้วยอินพุตเดิมไม่เปลี่ยนทิศที่หัน

ความหมายของฟิลด์เดิม:

| ฟิลด์ | ความหมาย / ค่าเมื่อไม่ระบุ |
| --- | --- |
| `id: 2` | หมายเลขถนนที่ใช้เชื่อมเส้น ต้องไม่ซ้ำ |
| `label: "2"` | ชื่อสำหรับแสดงใน Editor; ไม่ระบุจะใช้ id |
| `kind: "main"` | ถนนหลัก และเป็นค่าเริ่มต้น; เส้นเลี้ยวระบุ `"turn"` |
| `color: "#FFFFFF"` | รหัสสีขาวในข้อมูลเดิม แต่ SplineJson ไม่ได้อ่านฟิลด์นี้ สีจริงกำหนดจาก Material/การแสดงผลใน Editor จึงตัดออกได้ |
| `bidirectional: true` | ใช้เส้นเดียวได้ทั้ง S→E และ E→S; ค่าเริ่มต้นเป็น true และยังระบุ false ได้ |

`junction.json` ตัด label ที่ซ้ำ, kind ของถนนหลัก, bidirectional ที่เป็น true, รายการ node ว่าง, defaultRoad ที่เป็น 0 และ metadata สี/คำอธิบายที่ไม่ได้อ่านออกแล้ว ยังรองรับ JSON แบบเดิมที่ระบุฟิลด์เหล่านี้ครบ

ถนนโค้งเก็บ `points` แค่ต้นกับปลาย และ `controlPoint` หนึ่งจุด ตัวสร้าง spline ใช้ข้อมูลนี้สร้างโค้งเดิมได้โดยไม่ต้องเก็บจุดตัวอย่างระหว่างทาง ส่วน `mainRoad` ยังระบุปลายและจุดเชื่อมชัดเจน หลังแก้ JSON ใน Editor ให้กด **Refresh** เพื่อโหลดข้อมูลใหม่

## SplineResourceMesh — โมเดลจาก Resources

เปิด `Assets/Demo/SplineResourceMesh.unity` เพื่อทดลองโมเดลจริงและ Avatar ที่ควบคุมด้วย `splinemovement` ฉากนี้ใช้ `data/junction_test.json` และเริ่มที่ต้นถนน 1 เช่นเดิม

เพิ่ม **SplineResourceMesh** บน GameObject ที่มี **SplineJson** แล้วกด **Refresh Resource Roads** เพื่ออ่าน JSON สร้าง spline สำหรับเดิน และวางโมเดล มีช่อง **Junction Json** สำหรับเลือกไฟล์ หรือปล่อยว่างเพื่อโหลด **Resource Path** ซึ่งเริ่มต้นที่ `data/junction`

หากวัตถุเดิมยังไม่มี **SplineJson** ปุ่ม Refresh จะเพิ่มให้พร้อมรองรับ Undo โดยอัตโนมัติ ไม่จำเป็นต้องลบแล้วเพิ่ม SplineResourceMesh ใหม่

- โหลด `roads/intersectionRoad` วางตรงจุดตัดของช่วง `mainRoad` เช่น 1–2 กับ 3–4 โดยตรวจพิกัดจริง ไม่ผูกกับหมายเลขถนน
- ใช้ `roads/straightRoad` กับช่วงตรงและส่วนต่อขยาย โดยตัดส่วนที่โมเดลแยกครอบอยู่เพื่อไม่วางทับกัน
- ใช้ `roads/curvedRoad` กับถนนโค้งนอกแยกที่มี `controlPoint` หมุน/กลับด้านตามทิศและจัดปลายโมเดลให้ตรงกับ spline
- ถ้าไม่พบใน `Resources/roads/` จะโหลดจาก `Resources/road/` ซึ่งเป็นที่เก็บ FBX ของโปรเจกต์นี้

`junction_test.json` คงถนน 1–8 และเพิ่มเส้นทาง `3 → 9 (curve) → 11 (straight)` ทางซ้าย กับ `4 → 10 (curve) → 12 (straight)` ทางขวา ทั้งสองเส้นวิ่งกลับได้ พิกัดโค้งเลือกให้ตรงกับขนาดพอร์ตของ FBX ที่มี และทางตรงปลายสุดยาว 8 Unity units

โมเดลนี้รองรับจุดตัดตั้งฉากที่มีขนาดเท่ากัน และโค้งที่แนวต้น–controlPoint–ปลายหัก 90 องศา ค่า **Curve Entry / Curve Exit** เป็นจุดกึ่งกลางปลายของ curvedRoad ในพิกัดโมเดล ซึ่งตั้งไว้สำหรับ FBX ปัจจุบัน หากเปลี่ยนโมเดลให้ปรับค่าพอร์ตนี้ด้วย

การเดินยังอ้างอิง **SplineJson** ตัวเดิม: กำหนดช่อง **Spline Json** ของ `splinemovement` ไปยัง GameObject นี้ `SplineResourceMesh` ทำหน้าที่วางโมเดลและเรียก Refresh ให้ SplineJson จึงใช้ direction, history และขาไป–กลับชุดเดียวกัน

ทดสอบการวางโมเดล พื้นใต้เส้นเดิน รอยต่อโค้ง การกลับด้าน Undo และการเดินจริงใน Play Mode:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/Run-SplineJsonMovementValidation.ps1 -ValidationSuite Resources
```

## ตรวจสอบ

`Tests/Run-SplineJsonValidation.ps1` เปิด Unity แบบ batch ในโปรเจกต์ชั่วคราวแยกต่างหาก โดยใช้ package ที่มีในเครื่อง ตรวจการนำเข้า JSON, ทิศทาง, ขอบถนนไม่ขวางเส้นทาง, พื้นกลางแยกไม่เป็นรู, Undo, Duplicate, กดซ้ำ และเปิดฉากใหม่ พร้อมสร้างภาพเรนเดอร์ `mesh-preview.png`

```powershell
powershell -ExecutionPolicy Bypass -File Tests/Run-SplineJsonValidation.ps1
```
