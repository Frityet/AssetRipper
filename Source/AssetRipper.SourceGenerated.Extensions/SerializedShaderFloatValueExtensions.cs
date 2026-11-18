using AssetRipper.SourceGenerated.Subclasses.SerializedShaderFloatValue;

namespace AssetRipper.SourceGenerated.Extensions
{
	public static class SerializedShaderFloatValueExtensions
	{
        public static string PropertyReferenceName(this ISerializedShaderFloatValue value)
        {
            string name = value.Has_Name_R_FastPropertyName()
                ? value.Name_R_FastPropertyName.Name.String
                : value.Name_R_Utf8String.String;
            return string.IsNullOrEmpty(name) || name == "<noninit>" ? string.Empty : name;
        }
        public static bool HasPropertyReference(this ISerializedShaderFloatValue value) => value.PropertyReferenceName().Length != 0;
		public static bool IsZero(this ISerializedShaderFloatValue value) => value.Val == 0.0f;
		public static bool IsMax(this ISerializedShaderFloatValue value) => value.Val == 255.0f;
	}
}
