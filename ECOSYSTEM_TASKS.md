# VR Autism Ecosystem Development Tasks

## Phase 1: Vững chắc nền tảng (Foundation & Refactoring)
- [ ] Execute CODE_REFACTOR_PLAN.md
  - [ ] Dead code cleanup
  - [ ] Namespace standardisation
  - [ ] Singleton guard implementation
  - [ ] Data Models extraction
  - [ ] FirebasePaths constant implementation
  - [ ] Variable renaming
  - [ ] Encapsulation of QuestController

## Phase 2: Xây dựng Xương sống Dữ liệu (Cloud Architecture)
- [ ] Firebase Database Migration
  - [ ] Setup Firestore collections
  - [ ] Setup Realtime Database temporary nodes
  - [ ] Update Unity FirebaseManager for Firestore (historic data)
- [ ] Advanced Data Collection Logic (VR)
  - [ ] Create BehaviorLogger.cs
  - [ ] Implement advanced metrics tracking

## Phase 3: Ứng dụng Quản lý (Web Dashboard MVP)
- [ ] Project Setup
  - [ ] Initialize standard web framework (React/Next/Vue)
  - [ ] Setup UI library and theme
- [ ] Authentication & Authorization
  - [ ] Integrate Firebase Auth
  - [ ] Setup Role-based access (Expert/Parent)
- [ ] Dashboard Interface
  - [ ] Development of Child Profiles CRUD
  - [ ] Development of Sessions History View
  - [ ] Development of Analytics & Reports View

## Phase 4: Kết nối Real-time & Can thiệp (Live Interaction)
- [ ] PIN Pairing Mechanism
  - [ ] VR side: PIN generation & Realtime DB sync
  - [ ] Web side: PIN validation & linking
- [ ] Remote Parameter Loading
  - [ ] Fetch child profile settings on pairing
  - [ ] Apply parameters to Unity scene via GameManager
- [ ] Remote Control System
  - [ ] Web Dashboard Live Session control panel
  - [ ] Unity RemoteCommandHandler implementation
  - [ ] EventChannel integration for dynamic actions
