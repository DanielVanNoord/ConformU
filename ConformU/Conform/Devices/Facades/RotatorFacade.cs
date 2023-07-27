﻿using ASCOM.Common.DeviceInterfaces;

namespace ConformU
{
    public class RotatorFacade : FacadeBaseClass, IRotatorV4
    {
        // Create the test device in the facade base class
        public RotatorFacade(Settings conformSettings, ConformLogger logger) : base(conformSettings, logger) { }

        public bool CanReverse
        {
            get
            {
                return FunctionNoParameters<bool>(() => driver.CanReverse);
            }
        }

        public bool IsMoving
        {
            get
            {
                return FunctionNoParameters<bool>(() => driver.IsMoving);
            }
        }

        public float Position
        {
            get
            {
                return FunctionNoParameters<float>(() => driver.Position);
            }
        }

        public bool Reverse { get => FunctionNoParameters<bool>(() => driver.Reverse); set => Method1Parameter((i) => driver.Reverse = i, value); }

        public float StepSize
        {
            get
            {
                return FunctionNoParameters<float>(() => driver.StepSize);
            }
        }

        public float TargetPosition
        {
            get
            {
                return FunctionNoParameters<float>(() => driver.TargetPosition);
            }
        }

        public float MechanicalPosition
        {
            get
            {
                return FunctionNoParameters<float>(() => driver.MechanicalPosition);
            }
        }

        public void Halt()
        {
            MethodNoParameters(() => driver.Halt());
        }

        public void Move(float Position)
        {
            Method1Parameter((i) => driver.Move(i), Position);
        }

        public void MoveAbsolute(float Position)
        {
            Method1Parameter((i) => driver.MoveAbsolute(i), Position);
        }

        public void MoveMechanical(float Position)
        {
            Method1Parameter((i) => driver.MoveMechanical(i), Position);
        }

        public void Sync(float Position)
        {
            Method1Parameter((i) => driver.Sync(i), Position);
        }
    }
}
