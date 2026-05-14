# HOWTO: Dogpaddle และ Drag-N-Go

เอกสารนี้อธิบายการตั้งค่าและหลักการทำงานของระบบเดินปัจจุบันในโปรเจกต์

- `Assets/Scripts/TouchManager.cs`
- `Assets/Scripts/movement/dogpaddle_update.cs`
- `Assets/Scripts/movement/dragngo_improved.cs`

## ภาพรวม

ระบบอ่านค่าจาก touchpad ผ่าน `TouchpadManager`

ค่าที่ใช้หลักๆ คือ:

| ค่า | ใช้ทำอะไร |
|---|---|
| `IsTouching` | เช็กว่ามีนิ้วแตะอยู่หรือไม่ |
| `TouchCount` | จำนวนจุดสัมผัส |
| `PrimaryRawPosition` | ตำแหน่งนิ้วหลัก แบบ raw |
| `AverageRawPosition` | ค่าเฉลี่ยตำแหน่งนิ้วทั้งหมด ใช้กับ 2 นิ้ว |

## ค่าคำนวณร่วม

ในสคริปต์ movement ใช้ค่าปรับเทียบชุดเดียวกัน:

```csharp
private const float ScaleResearch = 65f / 40f;
private const float RawVerticalDistance = 1784f;
private const float RawHorizontalDistance = 4095f;
private const float TouchPadHorizontalCmDistance = 11f;
private const float TouchPadVerticalCmDistance = 6f;
private const float VerticalCmPerRaw = TouchPadVerticalCmDistance / RawVerticalDistance;
```

ความหมาย:

- ลากนิ้ว 40 cm ให้อวาตาร์เคลื่อนที่ 65 m
- ดังนั้น 1 cm = `65 / 40 = 1.625 m`
- touchpad แนว Y: `1784 raw = 6 cm`
- แปลง raw เป็น cm ด้วย `6 / 1784`

ตัวอย่าง:

```text
นิ้วลาก 10 cm
AvatarMoveDistance = (65 / 40) * 10
AvatarMoveDistance = 16.25 m
```

ถ้าค่า touchpad เป็น raw:

```text
DragDeltaY = Y raw ที่เปลี่ยนไป
dragYcm = DragDeltaY * (6 / 1784)
AvatarMoveDistance = dragYcm * (65 / 40)
```

## Dogpaddle

ไฟล์: `Assets/Scripts/movement/dogpaddle_update.cs`

แนวคิด:

```text
ลาก 1 นิ้วขึ้น/ลง = เดินหน้า/ถอยหลังทันที
แตะ 2 นิ้ว = หยุดเดิน และใช้ลากซ้าย/ขวาเพื่อหมุนโลก
```

### การตั้งค่าใน Inspector

ต้องใส่ reference เหล่านี้:

| Field | ใส่อะไร |
|---|---|
| `touchManager` | GameObject ที่มี `TouchpadManager` หรือปล่อยว่างให้หา `Instance` |
| `Player` | Avatar หรือ GameObject ที่ต้องเคลื่อนที่ |
| `worldRotateTarget` | Transform ที่ต้องการให้เป็นแกนอ้างอิงทิศทาง/การหมุน เช่น Camera Rig |

ถ้า `worldRotateTarget` ว่าง ระบบจะใช้ `Player.transform`

### เดิน 1 นิ้ว

สคริปต์จำตำแหน่ง raw ของนิ้วใน frame ก่อนหน้า:

```text
dragDeltaRaw = currentRawPosition - lastRawPosition
dragYcm = dragDeltaRaw.y * VerticalCmPerRaw
AvatarMoveDistance = dragYcm * ScaleResearch
```

จากนั้นขยับ Player:

```text
Player.position += moveTarget.forward * AvatarMoveDistance
```

ผลคือการเดินเป็นแบบ delta ต่อ frame:

- ลากขึ้น = เดินตาม `forward`
- ลากลง = ถอยหลัง
- หยุดลาก = หยุดเดิน

### หมุน 2 นิ้ว

เมื่อ `TouchCount >= 2`:

- เข้าโหมด `isTwoFingerMode`
- ปิดการเดิน 1 นิ้วชั่วคราว
- ใช้ `AverageRawPosition.x` เพื่อหมุน

สูตร:

```text
dragDeltaX = currentAverageX - lastAverageX
degreesPerRaw = 90 / 1784
rotationDegrees = -dragDeltaX * degreesPerRaw
```

ทิศทาง:

- ลากสองนิ้วไปซ้าย = หันขวา
- ลากสองนิ้วไปขวา = หันซ้าย

เมื่อปล่อยนิ้ว ระบบ reset state เพื่อกันการกระโดดของตำแหน่งตอนกลับมาเดิน

## Drag-N-Go

ไฟล์: `Assets/Scripts/movement/dragngo_improved.cs`

แนวคิด:

```text
Raycast หา NavPoint ก่อน
ถ้าเจอเป้า แสงเปลี่ยนเป็นสีแดง และแสดง Target
จากนั้นลาก 1 นิ้วเพื่อพา Avatar ไปยังตำแหน่ง Target
```

### การตั้งค่าใน Inspector

| Field | ใส่อะไร |
|---|---|
| `touchManager` | GameObject ที่มี `TouchpadManager` หรือปล่อยว่าง |
| `Player` | Avatar หรือ GameObject ที่ต้องเคลื่อนที่ |
| `RaycastOrigin` | จุดยิง Raycast เช่น Camera หรือ Controller |
| `worldRotateTarget` | Transform ที่หมุนตอนใช้ 2 นิ้ว |
| `lineRenderer` | เส้นแสดง Raycast ถ้าว่างจะสร้างให้อัตโนมัติ |
| `targetPrefab` | prefab เป้าหมาย ถ้าว่างจะหา `Assets/Prefabs/Target.prefab` |
| `navPointLayerMask` | Layer ของจุดเป้าหมาย |
| `maxRaycastDistance` | ระยะยิง Raycast |

ต้องมี Layer ชื่อ:

```text
NavPoint
```

และ object ที่เป็นจุดหมายต้องอยู่ใน Layer นี้

### Raycast และสีเส้น

ทุก frame ระบบยิง Raycast จาก:

```text
RaycastOrigin.position
RaycastOrigin.forward
```

ถ้าไม่ชน `NavPoint`:

- เส้นเป็นสีขาว
- ยังลากเพื่อเดินไม่ได้
- ซ่อน target

ถ้าชน `NavPoint`:

- เส้นเป็นสีแดง
- บันทึก `currentTargetPosition`
- แสดง `targetInstance`
- แตะ 1 นิ้วแล้วเริ่มลากไปหา target ได้

### เดิน 1 นิ้วแบบ Map ระยะ

Drag-N-Go ไม่ได้ใช้สูตร `cm * 1.625m` เพื่อเดินทีละนิดเหมือน Dogpaddle

แต่ใช้การ map ระยะลากบน touchpad เป็น progress จากจุดเริ่มไปถึง target:

```text
เริ่มแตะ:
dragStartRawPosition = ตำแหน่งนิ้วตอนเริ่ม
movementStartPosition = ตำแหน่ง Avatar ตอนเริ่ม

ตอนลาก:
totalDragRawY = currentRawY - dragStartRawY
availableRawY = ระยะ raw ที่เหลือถึงขอบ touchpad
progress = abs(totalDragRawY) / availableRawY
Player.position = Lerp(movementStartPosition, currentTargetPosition, progress)
```

ข้อสำคัญ:

- ถ้าเริ่มแตะกลาง touchpad แล้วลากสุด 3 cm จะถึง target
- ถ้าเริ่มแตะใกล้ขอบล่าง เหลือลากได้ 2 cm การลากสุด 2 cm ก็ถึง target
- ระยะ Avatar ถึง Target ถูก map กับระยะนิ้วที่เหลืออยู่
- ดังนั้นถ้าเหลือพื้นที่ลากน้อย Avatar จะเดินเร็วขึ้นเพื่อให้ถึง target เหมือนกัน

ตัวอย่าง:

```text
Avatar ห่าง Target = 20 m
เหลือพื้นที่ลาก = 6 cm
ลาก 3 cm = progress 50% = เดิน 10 m
ลาก 6 cm = progress 100% = ถึง Target
```

อีกกรณี:

```text
Avatar ห่าง Target = 20 m
เหลือพื้นที่ลาก = 2 cm
ลาก 1 cm = progress 50% = เดิน 10 m
ลาก 2 cm = progress 100% = ถึง Target
```

### หมุน 2 นิ้ว

ใช้หลักเดียวกับ Dogpaddle:

- `TouchCount >= 2` เข้าโหมดหมุน
- หยุด Drag-N-Go ชั่วคราว
- ใช้ `AverageRawPosition.x`

ทิศทาง:

- ลากสองนิ้วซ้าย = หันขวา
- ลากสองนิ้วขวา = หันซ้าย

## TouchpadManager

ไฟล์: `Assets/Scripts/TouchManager.cs`

หน้าที่:

- เริ่ม `TrackpadInterface.Start()`
- อ่าน `TouchpadContact` จาก queue
- เก็บ session ตาม `ContactId`
- ลบ session เมื่อ timeout
- ส่งตำแหน่ง raw ให้สคริปต์ movement ใช้

ค่าที่ movement ใช้:

```csharp
touchManager.IsTouching
touchManager.TouchCount
touchManager.PrimaryRawPosition
touchManager.AverageRawPosition
```

## วิธีทดสอบใน Unity

1. เปิด scene ที่มี `TouchpadManager`
2. ใส่ `dogpaddle_update` หรือ `dragngo_improved` ลง GameObject ควบคุม
3. ลาก `Player` ใส่ field
4. ตั้ง `worldRotateTarget`
5. สำหรับ Drag-N-Go ให้ตั้ง `RaycastOrigin`, `LineRenderer`, `Target Prefab`
6. สร้าง object เป้าหมาย และตั้ง Layer เป็น `NavPoint`
7. Play scene

ตรวจพฤติกรรม:

- Dogpaddle: ลาก 1 นิ้วขึ้น/ลงแล้ว Player เดินทันที
- Dogpaddle: แตะ 2 นิ้วแล้วลากซ้าย/ขวาเพื่อหมุน
- Drag-N-Go: เล็งไม่โดน NavPoint เส้นสีขาว
- Drag-N-Go: เล็งโดน NavPoint เส้นสีแดงและ target แสดง
- Drag-N-Go: แตะแล้วลาก 1 นิ้วเพื่อเดินไป target
- Drag-N-Go: แตะ 2 นิ้วแล้วหมุนได้เหมือน Dogpaddle

## ปัญหาที่พบบ่อย

### ลากแล้วไม่เดิน

เช็กว่า:

- `Player` ถูกใส่ใน Inspector แล้ว
- มี `TouchpadManager` ใน scene
- Drag-N-Go ต้องเล็งโดน object Layer `NavPoint` ก่อน

### Drag-N-Go ไม่มีเส้น

เช็กว่า:

- มี `LineRenderer` หรือให้สคริปต์สร้างเอง
- `RaycastOrigin` ไม่ว่าง
- `maxRaycastDistance` มากพอ

### Target ไม่แสดง

เช็กว่า:

- `targetPrefab` ถูกใส่ไว้
- หรือมี prefab ที่ path `Assets/Prefabs/Target.prefab`
- object เป้าหมายต้องอยู่ Layer `NavPoint`

### หมุนแล้วทิศกลับกัน

สูตรปัจจุบันตั้งใจให้:

```text
ลากสองนิ้วซ้าย = หันขวา
ลากสองนิ้วขวา = หันซ้าย
```

ถ้าต้องการกลับทิศ ให้เปลี่ยนเครื่องหมายที่บรรทัด:

```csharp
float rotationDegrees = -dragDeltaX * degreesPerRaw;
```
