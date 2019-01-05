﻿using FluentIL.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Linq;

namespace FluentIL
{
    public class MethodEditor
    {
        private readonly MethodDefinition _md;
        private readonly ExtendedTypeSystem _typeSystem;

        internal MethodEditor(MethodDefinition md)
        {
            _md = md;
            _md.Body?.SimplifyMacros();
            _typeSystem = md.Module.GetTypeSystem();
        }

        /// <summary>
        /// After Entry, Base Ctor Call anb before Aspect inits. Put aspect inits here.
        /// </summary>
        /// <param name="action"></param>
        public void OnInit(Action<PointCutOld> action)
        {
            var instruction = _md.IsConstructor && !_md.IsStatic ?
                FindBaseClassCtorCall() :
                GetMethodOriginalEntryPoint();

            var proc = _md.Body.GetEditor();

            action(new PointCutOld(proc, instruction));
        }

        /// <summary>
        /// After Entry, Base Ctor Call and Aspect inits.
        /// </summary>
        /// <param name="action"></param>
        public void OnEntry(Action<PointCutOld> action)
        {
            if (!_md.HasBody) return;

            var instruction = _md.IsConstructor && !_md.IsStatic ?
                FindBaseClassCtorCall() :
                GetMethodOriginalEntryPoint();

            action(new PointCutOld(_md.Body.GetEditor(), instruction));
        }

        public void OnExit(Action<PointCutOld> action)
        {
            if (!_md.HasBody) return;

            foreach (var ret in _md.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
            {
                var il = _md.Body.GetEditor();
                var newRet = il.Create(OpCodes.Ret);

                il.InsertAfter(ret, newRet);

                il.SafeReplace(ret, il.Create(OpCodes.Nop));

                action(new PointCutOld(_md.Body.GetEditor(), newRet));
            }
        }

        public void OnException(Action<PointCutOld> action)
        {
        }

        public void OnInstruction(Instruction instruction, Action<PointCutOld> action)
        {
            if (!_md.Body.Instructions.Contains(instruction))
                throw new ArgumentException("Wrong instruction.");

            action(new PointCutOld(_md.Body.GetEditor(), instruction));
        }

        public void Instead(Action<PointCutOld> action)
        {
            var proc = _md.Body.GetEditor();
            var instruction = proc.Create(OpCodes.Nop);

            _md.Body.Instructions.Clear();
            proc.Append(instruction);

            OnInstruction(instruction, action);

            proc.Remove(instruction);
        }

        protected Instruction FindBaseClassCtorCall()
        {
            var proc = _md.Body.GetEditor();

            if (!_md.IsConstructor)
                throw new Exception(_md.ToString() + " is not ctor.");

            if (_md.DeclaringType.IsValueType)
                return _md.Body.Instructions.First();

            var point = _md.Body.Instructions.FirstOrDefault(
                i => i != null && i.OpCode == OpCodes.Call && i.Operand is MethodReference
                    && ((MethodReference)i.Operand).Resolve().IsConstructor
                    && (((MethodReference)i.Operand).DeclaringType.Match(_md.DeclaringType.BaseType)
                        || ((MethodReference)i.Operand).DeclaringType.Match(_md.DeclaringType)));

            if (point == null)
                throw new Exception("Cannot find base class ctor call");

            return point.Next;
        }

        protected Instruction GetMethodOriginalEntryPoint()
        {
            return _md.Body.Instructions.First();
        }

        public void Mark(TypeReference attribute)
        {
            if (_md.CustomAttributes.Any(ca => ca.AttributeType.FullName == attribute.FullName))
                return;

            var constructor = _md.Module.ImportReference(attribute).Resolve()
                .Methods.First(m => m.IsConstructor && !m.IsStatic);

            _md.CustomAttributes.Add(new CustomAttribute(_typeSystem.Import(constructor)));
        }
    }
}