# ฐานข้อมูลราคาและแต้มการ์ด 90 ใบ (Data Table Format)

ตารางนี้ถูกออกแบบมาเพื่อ **"ให้ก็อปปี้ไปวางใน Excel หรือ Google Sheets"** ได้ทันที ข้อมูลถูกแบ่งคอลัมน์ให้อย่างชัดเจน (ไม่มีไอคอนหรือข้อความรกๆ ปนในตัวเลข) ทำให้กระบวนการนำไปเขียนโค้ดหรือทำ CSV ของเกมง่ายที่สุดครับ!

| ID | ชื่อการ์ด (Card Name) | หมวดสี | Tier | แต้ม | 🔴 ราคา CPU | 🔵 ราคา RAM | ⚪ ราคา STO | 🟣 ราคา NET | 🟡 ราคา SEC |
|:---|:---|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| 1 | Transistor | แดง (CPU) | 1 | 0 | - | 1 | 1 | 1 | 1 |
| 2 | Logic Gate | แดง (CPU) | 1 | 0 | - | 2 | 1 | - | - |
| 3 | ALU | แดง (CPU) | 1 | 0 | - | - | 2 | 2 | - |
| 4 | Clock Signal | แดง (CPU) | 1 | 0 | - | 2 | - | 1 | 2 |
| 5 | Assembler | แดง (CPU) | 1 | 0 | - | - | - | - | 3 |
| 6 | Syntax Structure | แดง (CPU) | 1 | 0 | 1 | - | 2 | 2 | - |
| 7 | Microprocessor | แดง (CPU) | 1 | 1 | - | 4 | - | - | - |
| 8 | Pipelining | แดง (CPU) | 1 | 1 | - | - | - | 2 | 2 |
| 9 | Task Scheduler | แดง (CPU) | 2 | 1 | - | 3 | 2 | 2 | - |
| 10 | Interrupt Handler | แดง (CPU) | 2 | 1 | - | 3 | - | - | 3 |
| 11 | Multi-Core | แดง (CPU) | 2 | 2 | - | - | 5 | - | - |
| 12 | Multithreading | แดง (CPU) | 2 | 2 | 4 | - | - | 2 | 1 |
| 13 | Hyper-Threading | แดง (CPU) | 2 | 4 | - | - | - | 5 | 3 |
| 14 | GPU | แดง (CPU) | 2 | 4 | - | 6 | - | - | - |
| 15 | Server Farm CPU | แดง (CPU) | 3 | 4 | - | 3 | 3 | 3 | 3 |
| 16 | Neural Processing Unit (NPU) | แดง (CPU) | 3 | 5 | 3 | - | 7 | - | - |
| 17 | Quantum Processor / QPU | แดง (CPU) | 3 | 5 | 2 | - | - | 6 | 2 |
| 18 | Supercomputer Cluster | แดง (CPU) | 3 | 7 | - | 4 | - | - | 7 |
| 19 | Bit | ฟ้า (RAM) | 1 | 0 | 1 | - | 1 | 1 | 1 |
| 20 | Variable | ฟ้า (RAM) | 1 | 0 | - | - | 2 | 1 | - |
| 21 | Pointer | ฟ้า (RAM) | 1 | 0 | - | - | - | 2 | 2 |
| 22 | CPU Registers | ฟ้า (RAM) | 1 | 0 | 2 | - | 2 | - | 1 |
| 23 | Static RAM / SRAM | ฟ้า (RAM) | 1 | 0 | 3 | - | - | - | - |
| 24 | Dynamic RAM / DRAM | ฟ้า (RAM) | 1 | 0 | - | 1 | - | 2 | 2 |
| 25 | Data Structure Basics | ฟ้า (RAM) | 1 | 1 | - | - | 4 | - | - |
| 26 | Cache Memory L1 | ฟ้า (RAM) | 1 | 1 | 2 | - | - | - | 2 |
| 27 | Memory Allocation | ฟ้า (RAM) | 2 | 1 | - | - | 3 | 2 | 2 |
| 28 | Garbage Collection | ฟ้า (RAM) | 2 | 1 | 3 | - | 3 | - | - |
| 29 | Virtual Memory | ฟ้า (RAM) | 2 | 2 | - | - | - | 5 | - |
| 30 | Paging & Swapping | ฟ้า (RAM) | 2 | 2 | 1 | 4 | - | - | 2 |
| 31 | L2/L3 Cache Architecture | ฟ้า (RAM) | 2 | 4 | 3 | - | - | - | 5 |
| 32 | Buffer Overflow Protection | ฟ้า (RAM) | 2 | 4 | - | - | 6 | - | - |
| 33 | Shared Memory Model | ฟ้า (RAM) | 3 | 4 | 3 | - | 3 | 3 | 3 |
| 34 | In-Memory Database | ฟ้า (RAM) | 3 | 5 | - | 3 | - | 7 | - |
| 35 | Distributed Cache | ฟ้า (RAM) | 3 | 5 | 2 | 2 | - | - | 6 |
| 36 | Holographic Memory | ฟ้า (RAM) | 3 | 7 | 7 | - | 4 | - | - |
| 37 | Text File | ขาว (Storage) | 1 | 0 | 1 | 1 | - | 1 | 1 |
| 38 | Directory | ขาว (Storage) | 1 | 0 | - | - | - | 2 | 1 |
| 39 | Basic Query | ขาว (Storage) | 1 | 0 | 2 | - | - | - | 2 |
| 40 | Magnetic Hard Disk / HDD | ขาว (Storage) | 1 | 0 | 1 | 2 | - | 2 | - |
| 41 | Solid State Drive / SSD | ขาว (Storage) | 1 | 0 | - | 3 | - | - | - |
| 42 | FAT Formatter | ขาว (Storage) | 1 | 0 | 2 | - | 1 | - | 2 |
| 43 | Relational Data Base | ขาว (Storage) | 1 | 1 | - | - | - | 4 | - |
| 44 | B-Tree Indexing | ขาว (Storage) | 1 | 1 | 2 | 2 | - | - | - |
| 45 | JSON Formatting | ขาว (Storage) | 2 | 1 | 2 | - | - | 3 | 2 |
| 46 | Data Compression | ขาว (Storage) | 2 | 1 | - | 3 | - | 3 | - |
| 47 | SQL Standards | ขาว (Storage) | 2 | 2 | - | - | - | - | 5 |
| 48 | NoSQL Database | ขาว (Storage) | 2 | 2 | 2 | 1 | 4 | - | - |
| 49 | RAID Array | ขาว (Storage) | 2 | 4 | 5 | 3 | - | - | - |
| 50 | Snapshot Recovery | ขาว (Storage) | 2 | 4 | - | - | - | 6 | - |
| 51 | Data Warehouse | ขาว (Storage) | 3 | 4 | 3 | 3 | - | 3 | 3 |
| 52 | Distributed File System | ขาว (Storage) | 3 | 5 | - | - | 3 | - | 7 |
| 53 | Cloud Object Storage | ขาว (Storage) | 3 | 5 | 6 | 2 | 2 | - | - |
| 54 | Big Data Data Center | ขาว (Storage) | 3 | 7 | - | 7 | - | 4 | - |
| 55 | LAN Cable | ม่วง (Network) | 1 | 0 | 1 | 1 | 1 | - | 1 |
| 56 | MAC Address | ม่วง (Network) | 1 | 0 | 1 | - | - | - | 2 |
| 57 | Network Hub | ม่วง (Network) | 1 | 0 | 2 | 2 | - | - | - |
| 58 | IP Address | ม่วง (Network) | 1 | 0 | - | 1 | 2 | - | 2 |
| 59 | Binary Packet | ม่วง (Network) | 1 | 0 | - | - | 3 | - | - |
| 60 | Modem | ม่วง (Network) | 1 | 0 | 2 | 2 | - | 1 | - |
| 61 | Switch | ม่วง (Network) | 1 | 1 | - | - | - | - | 4 |
| 62 | HTTP Protocol | ม่วง (Network) | 1 | 1 | - | 2 | 2 | - | - |
| 63 | DHCP Server | ม่วง (Network) | 2 | 1 | 2 | 2 | - | - | 3 |
| 64 | DNS Registry | ม่วง (Network) | 2 | 1 | - | - | 3 | - | 3 |
| 65 | Router | ม่วง (Network) | 2 | 2 | 5 | - | - | - | - |
| 66 | TCP / IP Protocol | ม่วง (Network) | 2 | 2 | - | 2 | 1 | 4 | - |
| 67 | Load Balancer | ม่วง (Network) | 2 | 4 | - | 5 | 3 | - | - |
| 68 | Web Socket | ม่วง (Network) | 2 | 4 | - | - | - | - | 6 |
| 69 | CDN | ม่วง (Network) | 3 | 4 | 3 | 3 | 3 | - | 3 |
| 70 | Cloud Mesh Network | ม่วง (Network) | 3 | 5 | 7 | - | - | 3 | - |
| 71 | Optical Fiber Backbone | ม่วง (Network) | 3 | 5 | - | 6 | 2 | 2 | - |
| 72 | IPFS | ม่วง (Network) | 3 | 7 | - | - | 7 | - | 4 |
| 73 | Username Password | ทอง (Security) | 1 | 0 | 1 | 1 | 1 | 1 | - |
| 74 | Antivirus | ทอง (Security) | 1 | 0 | 2 | 1 | - | - | - |
| 75 | Plaintext | ทอง (Security) | 1 | 0 | - | 2 | 2 | - | - |
| 76 | Access Token | ทอง (Security) | 1 | 0 | 2 | - | 1 | 2 | - |
| 77 | System Logs | ทอง (Security) | 1 | 0 | - | - | - | 3 | - |
| 78 | Port Blocking | ทอง (Security) | 1 | 0 | - | 2 | 2 | - | 1 |
| 79 | Salting Password | ทอง (Security) | 1 | 1 | 4 | - | - | - | - |
| 80 | Symmetric Encryption | ทอง (Security) | 1 | 1 | - | - | 2 | 2 | - |
| 81 | Asymmetric Key / RSA | ทอง (Security) | 2 | 1 | 3 | 2 | 2 | - | - |
| 82 | Firewall Rules | ทอง (Security) | 2 | 1 | 3 | - | - | 3 | - |
| 83 | HTTPS / SSL | ทอง (Security) | 2 | 2 | - | 5 | - | - | - |
| 84 | DDoS Mitigation | ทอง (Security) | 2 | 2 | - | - | 2 | 1 | 4 |
| 85 | Two-Factor Auth / 2FA | ทอง (Security) | 2 | 4 | - | - | 5 | 3 | - |
| 86 | Penetration Testing | ทอง (Security) | 2 | 4 | 6 | - | - | - | - |
| 87 | JWT (JSON Web Token) | ทอง (Security) | 3 | 4 | 3 | 3 | 3 | 3 | - |
| 88 | Blockchain Ledger | ทอง (Security) | 3 | 5 | - | 7 | - | - | 3 |
| 89 | End-to-End Encryption | ทอง (Security) | 3 | 5 | - | - | 6 | 2 | 2 |
| 90 | Zero Trust Architecture | ทอง (Security) | 3 | 7 | 4 | - | - | 7 | - |
