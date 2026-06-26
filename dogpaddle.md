# dogpaddle.cs ทำอะไร

`dogpaddle` เป็น `MonoBehaviour` ที่ใช้รับข้อมูลการสัมผัสจาก `TouchpadManager` แล้วแปลงการลากนิ้วให้กลายเป็นการเคลื่อนที่หรือการหมุนวัตถุ `Player`

## ตัวแปรสำคัญ

- `touchManager` อ้างอิงไปยัง `TouchpadManager`
- `Player` คือวัตถุที่ถูกขยับตำแหน่ง
- `worldRotateTarget` คือเป้าหมายที่ใช้เป็นทิศอ้างอิงในการเดินและเป็นวัตถุที่ถูกหมุน ถ้าไม่ได้กำหนดจะใช้ `Player.transform`
- `TouchCount` ใช้นับจำนวนเฟรมที่มีการแตะต่อเนื่อง
- `lastPosition` เก็บตำแหน่งสัมผัสของเฟรมก่อนหน้า
- `suppressNextDragFrame` ใช้ข้ามการคำนวณ delta หนึ่งเฟรมหลังเริ่มแตะหรือเปลี่ยนโหมด เพื่อกันค่ากระโดด

## ลำดับการทำงาน

สคริปต์ทำงานหลักใน `FixedUpdate()`

1. ถ้า `touchManager` ยังไม่ถูกกำหนด จะพยายามดึงจาก `TouchpadManager.Instance`
2. ถ้าไม่มี `touchManager` หรือไม่มี `Player` จะหยุดทำงานทันที
3. ถ้าไม่มีการแตะ (`IsTouching == false`)
   - รีเซ็ต `TouchCount`
   - ล้าง `lastPosition`
   - ปิด `suppressNextDragFrame`
4. ถ้ามีการแตะ จะอ่านค่า
   - `mode` จาก `CurrentMode`
   - `status` จาก `Status`
   - `currentPosition` จาก `GetCurrentTouch()`
5. ถ้าเป็นสถานะเริ่มแตะ (`OnTouch`) หรือมีการเปลี่ยนโหมด (`TouchMode.Change`)
   - ตั้ง `TouchCount = 1`
   - บันทึก `lastPosition`
   - เปิด `suppressNextDragFrame`
   - ยังไม่คำนวณการเคลื่อนที่ในเฟรมนั้น
6. ถ้าไม่ได้อยู่ในสถานะลาก (`OnDrag`) จะอัปเดต `lastPosition` แล้วรอเฟรมถัดไป
7. ถ้ายังเป็นช่วงต้นของการลาก หรือยังไม่มี `lastPosition` จะยังไม่คำนวณ delta
8. ถ้า `suppressNextDragFrame` เป็น `true` จะข้ามการคำนวณหนึ่งเฟรม แล้วปิด flag นี้
9. เมื่อพร้อมคำนวณ จะหา `dragDelta = currentPosition - lastPosition`
10. จากนั้นแยกการทำงานตามโหมด
    - `Rotate` ใช้ลากแนวนอนเพื่อหมุน
    - `Translate` ใช้ลากแนวตั้งเพื่อเคลื่อนที่ไปหน้า/หลัง

## การเคลื่อนที่แบบ Translate

เมื่อ `mode == Translate` จะเรียก `MoveByOneFingerDrag(dragDelta)`

- ถ้า `dragDelta.y` เป็น 0 จะไม่ทำอะไร
- ถ้ามีการลากในแกน Y จะเรียก `MoveForwardBackward(dragDelta.y)`

ใน `MoveForwardBackward()`

1. แปลงค่า raw ของ touchpad เป็นเซนติเมตรด้วย `VerticalCmPerRaw`
2. คูณด้วย `scaleResearch`
3. เลือกทิศทางจาก `worldRotateTarget.forward` ถ้ามี `worldRotateTarget`
4. ถ้าไม่มี `worldRotateTarget` จะใช้ `Player.transform.forward`
5. ขยับ `Player.transform.position` ไปตามทิศ forward

ผลลัพธ์คือ:

- ลากขึ้น/ลง ทำให้ `Player` เคลื่อนที่ไปข้างหน้า/ถอยหลัง
- ทิศทางการเดินอิงจากวัตถุหมุนอ้างอิง ไม่จำเป็นต้องอิงจากแกนโลกตรง ๆ

## การหมุนแบบ Rotate

เมื่อ `mode == Rotate` จะตรวจสอบก่อนว่าเป็นการลากแนวนอนจริงหรือไม่ ด้วย `IsHorizontalRotateDrag(dragDelta)`

เงื่อนไขคือ:

- `abs(dragDelta.x)` ต้องมากกว่า `RotateDeadZoneRaw`
- `abs(dragDelta.x)` ต้องมากกว่า `abs(dragDelta.y)`

ถ้าผ่านเงื่อนไข จะเรียก `RotateByTwoFingerDrag(dragDelta)`

ใน `RotateByTwoFingerDrag()`

- เลือกวัตถุที่จะหมุนเป็น `worldRotateTarget`
- ถ้าไม่มี จะหมุน `Player.transform`
- หมุนรอบแกน `Vector3.up`
- ใช้ `Space.World`
- ค่ามุมหมุนคือ `-dragDelta.x * (90f / RawVerticalDistance)`

ผลลัพธ์คือ:

- การลากซ้ายขวาจะทำให้วัตถุหมุนซ้ายขวาในแกนโลก
- มี dead zone เพื่อกันการหมุนจากการสั่นหรือการลากเล็กน้อย

## ค่าคงที่ที่ใช้คำนวณ

- `scaleResearch = 65f / 40f` ใช้เป็นตัวคูณระยะเคลื่อนที่
- `RawVerticalDistance = 912f` ใช้เป็นช่วง raw อ้างอิงของ touchpad
- `TouchPadVerticalCmDistance = 8f` ระยะจริงแนวตั้งของ touchpad เป็นเซนติเมตร
- `VerticalCmPerRaw = 8 / 912` ใช้แปลงค่า raw เป็นระยะจริง
- `RotateDeadZoneRaw = 8f` ใช้ตัดการหมุนที่เล็กเกินไป

## สรุปสั้น

สคริปต์ `dogpaddle` ทำหน้าที่แปลง gesture จาก touchpad ให้เป็นการควบคุมตัวละคร/วัตถุ:

- โหมด `Translate` ลากขึ้นลงเพื่อเดินหน้าและถอยหลัง
- โหมด `Rotate` ลากซ้ายขวาเพื่อหมุนทิศทาง
- มีการกันค่า delta กระโดดตอนเริ่มแตะหรือเปลี่ยนโหมด
- รองรับการใช้ `worldRotateTarget` เป็นตัวกำหนดทิศและแกนหมุน
