# HOWTO: Touchpad Movement

คู่มือนี้อธิบายการใช้งานระบบเดินจาก touchpad ในโปรเจกต์นี้ โดยอิงจากไฟล์ปัจจุบัน:

- `Assets/Scripts/TouchManager.cs`
- `Assets/Scripts/movement/dogpaddle_update.cs`
- `Assets/Scripts/movement/dragngo_improved.cs`

## 1. TouchpadManager

`TouchpadManager` เป็นตัวกลางรับข้อมูลจาก trackpad แล้วส่งค่าให้ระบบเดินใช้

ค่าที่สคริปต์ movement ใช้บ่อย:

| Property | ความหมาย |
|---|---|
| `IsTouching` | มีนิ้วแตะอยู่หรือไม่ |
| `TouchCount` | จำนวนจุดสัมผัส |
| `PrimaryRawPosition` | ตำแหน่งนิ้วหลักแบบ raw |
| `AverageRawPosition` | ค่าเฉลี่ยตำแหน่งนิ้วทั้งหมด ใช้ตอน 2 นิ้ว |

ใน scene ควรมี GameObject ที่ติด `TouchpadManager` ไว้ 1 ตัว

## 2. ค่าปรับเทียบ

ทั้ง `dogpaddle_update.cs` และ `dragngo_improved.cs` ใช้ค่าปรับเทียบชุดเดียวกัน:

```csharp
private const float ScaleResearch = 65f / 40f;
private const float RawVerticalDistance = 1784f;
private const float TouchPadVerticalCmDistance = 6f;
private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
```

ความหมาย:

```text
ลากนิ้ว 40 cm = Avatar เคลื่อนที่ 65 m
ดังนั้น 1 cm = 65 / 40 = 1.625 m

1784 raw = 6 cm
ดังนั้น 1 raw = 6 / 1784 cm
```

สูตรพื้นฐาน:

```text
dragYcm = DragDeltaY * (6 / 1784)
AvatarMoveDistance = dragYcm * (65 / 40)
```

ตัวอย่าง:

```text
ลากนิ้ว 10 cm
AvatarMoveDistance = 10 * (65 / 40)
AvatarMoveDistance = 16.25 m
```

## 3. Dogpaddle

ไฟล์:

```text
Assets/Scripts/movement/dogpaddle_update.cs
```

แนวคิด:

```text
1 นิ้ว = เดินหน้า/ถอยหลัง
2 นิ้ว = หยุดเดิน แล้วหมุนโลก
```

### ตั้งค่าใน Inspector

ใส่ค่าเหล่านี้ใน component `dogpaddle_update`

| Field | ค่า |
|---|---|
| `touchManager` | ตัว `TouchpadManager` ถ้าว่างจะหา `TouchpadManager.Instance` |
| `Player` | Avatar ที่ต้องการขยับ |
| `worldRotateTarget` | Transform ที่ใช้เป็นทิศ forward และใช้หมุนโลก |

ถ้า `worldRotateTarget` ว่าง จะใช้ `Player.transform`

### การเดิน 1 นิ้ว

สคริปต์จำตำแหน่งนิ้ว frame ก่อนหน้า แล้วหาค่า delta:

```text
dragDeltaRaw = currentRawPosition - lastRawPosition
dragYcm = dragDeltaRaw.y * VerticalCmPerRaw
AvatarMoveDistance = dragYcm * ScaleResearch
```

จากนั้นขยับ Avatar:

```text
Player.position += moveTarget.forward * AvatarMoveDistance
```

ผลลัพธ์:

- ลากขึ้น = เดินหน้า
- ลากลง = ถอยหลัง
- ระยะเดินขึ้นกับระยะนิ้วที่ลากจริง

### การหมุน 2 นิ้ว

เมื่อ `TouchCount >= 2` ระบบจะเข้าโหมดหมุน:

```text
currentRawPosition = touchManager.AverageRawPosition
dragDeltaX = currentRawPosition.x - lastTwoFingerRawPosition.x
rotationDegrees = -dragDeltaX * (90 / 1784)
```

ทิศทางที่ตั้งไว้:

```text
ลากสองนิ้วซ้าย = หันขวา
ลากสองนิ้วขวา = หันซ้าย
```

เมื่อปล่อยนิ้ว ระบบ reset state เพื่อไม่ให้ Avatar กระโดดตอนกลับไปเดิน 1 นิ้ว

## 4. Drag-N-Go

ไฟล์:

```text
Assets/Scripts/movement/dragngo_improved.cs
```

แนวคิด:

```text
Raycast หา NavPoint
ถ้าเจอ NavPoint ให้แสดง target และเปลี่ยนเส้นเป็นสีแดง
จากนั้นลาก 1 นิ้วเพื่อพา Avatar ไปถึง target
```

### ตั้งค่าใน Inspector

ใส่ค่าเหล่านี้ใน component `dragngo_improved`

| Field | ค่า |
|---|---|
| `touchManager` | ตัว `TouchpadManager` ถ้าว่างจะหา `TouchpadManager.Instance` |
| `Player` | Avatar ที่ต้องการขยับ |
| `RaycastOrigin` | จุดยิง Raycast เช่น Camera |
| `worldRotateTarget` | Transform ที่ใช้หมุนโลกตอน 2 นิ้ว |
| `lineRenderer` | เส้นแสดง Raycast ถ้าว่างจะสร้างเอง |
| `targetPrefab` | Prefab เป้าหมาย |
| `navPointLayerMask` | Layer ของจุดหมาย |
| `maxRaycastDistance` | ระยะยิง Raycast |
| `playerGroundOffset` | ยก target ขึ้น/ลงจากจุดชน |

ต้องมี Layer ชื่อ:

```text
NavPoint
```

Object ที่ใช้เป็นจุดหมายต้องอยู่ใน Layer `NavPoint`

### Raycast

ทุก frame ระบบยิง Raycast จาก:

```text
RaycastOrigin.position
RaycastOrigin.forward
```

ถ้าไม่ชน `NavPoint`:

- `hasTarget = false`
- เส้นเป็นสีขาว
- ซ่อน target
- แตะลากแล้วไม่เดินไป target

ถ้าชน `NavPoint`:

- `hasTarget = true`
- เส้นเป็นสีแดง
- บันทึก `currentTargetPosition`
- แสดง `targetInstance`

### Target Prefab

สคริปต์จะสร้าง `targetInstance` ด้วยลำดับนี้:

1. ใช้ `targetPrefab` จาก Inspector
2. ถ้าว่าง จะโหลด `Resources.Load<GameObject>("Target")`
3. ใน Unity Editor ถ้ายังว่าง จะโหลด `Assets/Prefabs/Target.prefab`
4. ถ้ายังไม่มี จะสร้าง Sphere ให้แทน

## 5. Drag-N-Go: วิธี Map ระยะลากไป Target

ระบบ Drag-N-Go ไม่ได้เดินด้วยสูตร `dragYcm * 1.625` แบบ Dogpaddle

แต่ใช้หลักนี้:

```text
ระยะลากที่เหลือบน touchpad = 0% ถึง 100%
ระยะจาก Avatar ถึง Target = 0% ถึง 100%
```

ตอนเริ่มแตะ:

```text
dragStartRawPosition = ตำแหน่งนิ้วตอนเริ่ม
movementStartPosition = ตำแหน่ง Avatar ตอนเริ่ม
currentTargetPosition = ตำแหน่ง target จาก Raycast
```

ตอนลาก:

```text
totalDragRawY = currentRawY - dragStartRawY

ถ้าลากขึ้น:
availableRawY = 1784 - dragStartRawY

ถ้าลากลง:
availableRawY = dragStartRawY

progress = abs(totalDragRawY) / availableRawY
progress = clamp(progress, 0, 1)

Player.position = Lerp(movementStartPosition, currentTargetPosition, progress)
```

ความหมาย:

- ลากถึงขอบ touchpad = ถึง target
- เริ่มแตะกลาง touchpad เหลือลาก 3 cm ก็ลาก 3 cm ถึง target
- เริ่มแตะใกล้ขอบล่าง เหลือลาก 2 cm ก็ลาก 2 cm ถึง target
- ถ้าพื้นที่ลากเหลือน้อย Avatar จะเดินเร็วขึ้น

ตัวอย่าง:

```text
Avatar ห่าง Target 20 m
เหลือพื้นที่ลาก 6 cm
ลาก 3 cm = progress 50% = เดิน 10 m
ลาก 6 cm = progress 100% = ถึง Target
```

อีกตัวอย่าง:

```text
Avatar ห่าง Target 20 m
เหลือพื้นที่ลาก 2 cm
ลาก 1 cm = progress 50% = เดิน 10 m
ลาก 2 cm = progress 100% = ถึง Target
```

## 6. Drag-N-Go: หมุน 2 นิ้ว

Drag-N-Go ใช้การหมุน 2 นิ้วเหมือน Dogpaddle

เมื่อ `TouchCount >= 2`:

- หยุดการลากไป target
- เข้าโหมดหมุน
- ใช้ `AverageRawPosition.x`

สูตร:

```text
rotationDegrees = -dragDeltaX * (90 / 1784)
```

ทิศทาง:

```text
ลากสองนิ้วซ้าย = หันขวา
ลากสองนิ้วขวา = หันซ้าย
```

## 7. วิธีทดสอบ

### ทดสอบ Dogpaddle

1. มี GameObject ที่ติด `TouchpadManager`
2. สร้าง GameObject แล้วติด `dogpaddle_update`
3. ใส่ `Player`
4. ใส่ `worldRotateTarget`
5. Play scene
6. ลาก 1 นิ้วขึ้น/ลงเพื่อเดิน
7. ลาก 2 นิ้วซ้าย/ขวาเพื่อหมุน

### ทดสอบ Drag-N-Go

1. มี GameObject ที่ติด `TouchpadManager`
2. สร้าง GameObject แล้วติด `dragngo_improved`
3. ใส่ `Player`
4. ใส่ `RaycastOrigin`
5. ใส่หรือปล่อยให้สร้าง `LineRenderer`
6. สร้าง object จุดหมาย และตั้ง Layer เป็น `NavPoint`
7. เล็งให้ Raycast ชน `NavPoint`
8. เห็นเส้นแดงและ target
9. แตะ 1 นิ้ว แล้วลากเพื่อเดินไป target
10. แตะ 2 นิ้ว แล้วลากซ้าย/ขวาเพื่อหมุน

## 8. ปัญหาที่พบบ่อย

### ไม่มีการเดิน

เช็ก:

- มี `TouchpadManager` ใน scene หรือไม่
- `Player` ถูกใส่ใน Inspector หรือไม่
- สำหรับ Drag-N-Go ต้องเล็งโดน Layer `NavPoint` ก่อน

### เส้นไม่เปลี่ยนเป็นสีแดง

เช็ก:

- Object เป้าหมายอยู่ Layer `NavPoint`
- `navPointLayerMask` ถูกตั้งไว้ หรือมี Layer ชื่อ `NavPoint`
- `RaycastOrigin.forward` หันไปทางเป้าหมาย
- `maxRaycastDistance` มากพอ

### Target ไม่ขึ้น

เช็ก:

- ใส่ `targetPrefab` แล้วหรือไม่
- มี prefab ที่ `Assets/Prefabs/Target.prefab` หรือไม่
- Raycast ชน `NavPoint` จริงหรือไม่

### หมุนผิดทิศ

บรรทัดนี้เป็นตัวกำหนดทิศ:

```csharp
float rotationDegrees = -dragDeltaX * degreesPerRaw;
```

ถ้าต้องการกลับทิศ ให้เอาเครื่องหมาย `-` ออก

## 9. สรุปสั้น

Dogpaddle:

```text
DragDeltaY raw -> cm -> meter -> เดินตาม forward
```

Drag-N-Go:

```text
Raycast ชน NavPoint -> ได้ target
ระยะลากที่เหลือบน touchpad -> progress 0 ถึง 1
Lerp จาก Avatar ไป target
```

2 นิ้ว:

```text
AverageRawPosition.x -> dragDeltaX -> หมุนโลก
```
