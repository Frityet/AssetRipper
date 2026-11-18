using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Optimizers
{
    /// <summary>
    /// Comparison masks are represented by the HLSL emitter as float 0/1 rather
    /// than DXBC's zero/all-one bits. Only proven mask lanes may be simplified:
    /// NOT becomes logical complement and mask AND value becomes conditional
    /// selection, preserving fractional values and either operand order.
    /// </summary>
    public class USILAndOptimizer : IUSILOptimizer
    {
        private sealed class Branch
        {
            public required HashSet<string> Before;
            public HashSet<string>? Then;
        }

        public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
        {
            bool changed = false;
            var predicates = new HashSet<string>();
            var branches = new Stack<Branch>();
            var loopWrites = new Stack<HashSet<string>>();
            var instructions = shader.instructions;
            for (int index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (instruction.commented) continue;
                switch (instruction.instructionType)
                {
                    case USILInstructionType.IfTrue:
                    case USILInstructionType.IfFalse:
                        branches.Push(new Branch { Before = new HashSet<string>(predicates) });
                        continue;
                    case USILInstructionType.Else:
                        if (branches.Count == 0) { predicates.Clear(); continue; }
                        branches.Peek().Then = new HashSet<string>(predicates);
                        predicates = new HashSet<string>(branches.Peek().Before);
                        continue;
                    case USILInstructionType.EndIf:
                        if (branches.Count == 0) { predicates.Clear(); continue; }
                        var branch = branches.Pop();
                        predicates.IntersectWith(branch.Then ?? branch.Before);
                        continue;
                    case USILInstructionType.Loop:
                    case USILInstructionType.ForLoop:
                        // A loop may run zero/many times, or exit via break. Do
                        // not infer a loop-carried predicate from linear order.
                        var writes = FindLoopWrites(instructions, index);
                        loopWrites.Push(writes);
                        predicates.ExceptWith(writes);
                        continue;
                    case USILInstructionType.EndLoop:
                        if (loopWrites.Count == 0) predicates.Clear();
                        else predicates.ExceptWith(loopWrites.Pop());
                        continue;
                }

                var destination = instruction.destOperand;
                if (destination == null) continue;
                var sources = instruction.srcOperands;
                // Snapshot source classification before invalidating destination:
                // DXBC permits overlapping source/destination registers/swizzles.
                bool[] result = new bool[destination.mask.Length];
                for (int lane = 0; lane < result.Length; lane++)
                {
                    bool Predicate(int source) => source < sources.Count && IsPredicate(sources[source], lane, predicates);
                    result[lane] = instruction.IsComparisonType() || instruction.instructionType switch
                    {
                        USILInstructionType.Move => Predicate(0),
                        USILInstructionType.MoveConditional =>
                            (Predicate(1) && (Predicate(2) || IsZero(sources[2], lane))) ||
                            (Predicate(2) && IsZero(sources[1], lane)),
                        USILInstructionType.Not or USILInstructionType.LogicalNot => Predicate(0),
                        USILInstructionType.And or USILInstructionType.Or or USILInstructionType.Xor => Predicate(0) && Predicate(1),
                        _ => false
                    };
                }

                bool AllPredicate(int source) => result.Length > 0 && source < sources.Count &&
                    Enumerable.Range(0, result.Length).All(lane => IsPredicate(sources[source], lane, predicates));
                if (instruction.instructionType == USILInstructionType.Not && AllPredicate(0))
                {
                    instruction.instructionType = USILInstructionType.LogicalNot;
                    changed = true;
                }
                else if (instruction.instructionType == USILInstructionType.And && (AllPredicate(0) || AllPredicate(1)))
                {
                    bool left = AllPredicate(0);
                    instruction.instructionType = USILInstructionType.MoveConditional;
                    instruction.srcOperands = new List<USILOperand> { sources[left ? 0 : 1], sources[left ? 1 : 0], new USILOperand(0f) };
                    changed = true;
                }

                // Every ordinary write kills prior knowledge, including partial
                // writes, arithmetic, loads and numeric constants. This is vital:
                // register allocation reuses former predicate lanes for floats.
                string[] keys = Keys(destination).ToArray();
                predicates.ExceptWith(keys);
                for (int lane = 0; lane < keys.Length; lane++)
                    if (result[lane]) predicates.Add(keys[lane]);
            }
            return changed;
        }

        private static bool IsPredicate(USILOperand operand, int lane, HashSet<string> predicates)
        {
            if (operand.negative || operand.absoluteValue || operand.arrayRelative != null ||
                operand.operandType != USILOperandType.TempRegister || operand.mask.Length == 0) return false;
            int index = operand.mask.Length == 1 ? 0 : lane;
            return index < operand.mask.Length && predicates.Contains(Key(operand, operand.mask[index]));
        }

        private static bool IsZero(USILOperand operand, int lane)
        {
            if (operand.negative || operand.absoluteValue) return false;
            // A numeric zero is also an all-zero mask, but only admit it as the
            // other arm of a movc whose first arm is independently proven mask.
            if (operand.operandType == USILOperandType.ImmediateFloat)
            {
                int i = operand.immValueFloat.Length == 1 ? 0 : lane;
                return i < operand.immValueFloat.Length && BitConverter.SingleToInt32Bits(operand.immValueFloat[i]) == 0;
            }
            if (operand.operandType == USILOperandType.ImmediateInt)
            {
                int i = operand.immValueInt.Length == 1 ? 0 : lane;
                return i < operand.immValueInt.Length && operand.immValueInt[i] == 0;
            }
            return false;
        }

        private static string Key(USILOperand operand, int lane) => $"{operand.registerIndex}.{lane}";
        private static IEnumerable<string> Keys(USILOperand operand) =>
            operand.operandType == USILOperandType.TempRegister && operand.arrayRelative == null
                ? operand.mask.Select(lane => Key(operand, lane)) : Enumerable.Empty<string>();

        private static HashSet<string> FindLoopWrites(List<USILInstruction> instructions, int start)
        {
            var result = new HashSet<string>();
            int depth = 1;
            for (int i = start + 1; i < instructions.Count && depth > 0; i++)
            {
                var instruction = instructions[i];
                if (instruction.commented) continue;
                if (instruction.instructionType == USILInstructionType.Loop || instruction.instructionType == USILInstructionType.ForLoop) depth++;
                if (instruction.instructionType == USILInstructionType.EndLoop) depth--;
                if (instruction.destOperand != null) result.UnionWith(Keys(instruction.destOperand));
            }
            return result;
        }
    }
}
