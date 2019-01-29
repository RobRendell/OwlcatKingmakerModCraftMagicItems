using System;
using System.Linq;
using System.Reflection;

namespace CraftMagicItems {
    public delegate object FastGetter(object source);

    public delegate TResult FastGetter<in TClass, out TResult>(TClass source);

    public delegate void FastSetter(object source, object value);

    public delegate void FastSetter<in TClass, in TValue>(TClass source, TValue value);

    public delegate object FastInvoker(object target, params object[] parameters);

    public delegate TResult FastInvoker<in TClass, out TResult>(TClass target);

    public delegate TResult FastInvoker<in TClass, in T1, out TResult>(TClass target, T1 arg1);

    public delegate TResult FastInvoker<in TClass, in T1, in T2, out TResult>(TClass target, T1 arg1, T2 arg2);

    public delegate TResult FastInvoker<in TClass, in T1, in T2, in T3, out TResult>(TClass target, T1 arg1, T2 arg2, T3 arg3);

    public delegate object FastStaticInvoker(params object[] parameters);

    public delegate TResult FastStaticInvoker<out TResult>();

    public delegate TResult FastStaticInvoker<in T1, out TResult>(T1 arg1);

    public delegate TResult FastStaticInvoker<in T1, in T2, out TResult>(T1 arg1, T2 arg2);

    public delegate TResult FastStaticInvoker<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);

    public class Accessors {
        public static Harmony12.AccessTools.FieldRef<TClass, TResult> CreateFieldRef<TClass, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var fieldInfo = Harmony12.AccessTools.Field(classType, name);
            if (fieldInfo == null) {
                throw new Exception($"{classType} does not contain field {name}");
            }

            if (!resultType.IsAssignableFrom(fieldInfo.FieldType)) {
                throw new InvalidCastException($"Cannot cast field type {resultType} as {fieldInfo.FieldType} for class {classType} field {name}");
            }

            var fieldRef = Harmony12.AccessTools.FieldRefAccess<TClass, TResult>(name);
            return fieldRef;
        }

        public static FastGetter CreateGetter(Type classType, Type resultType, string name) {
            var fieldInfo = Harmony12.AccessTools.Field(classType, name);
            var propInfo = Harmony12.AccessTools.Property(classType, name);
            if (fieldInfo == null && propInfo == null) {
                throw new Exception($"{classType} does not contain field or property {name}");
            }

            bool isProp = propInfo != null;
            Type memberType = isProp ? propInfo.PropertyType : fieldInfo.FieldType;
            string memberTypeName = isProp ? "property" : "field";
            if (!resultType.IsAssignableFrom(memberType)) {
                throw new InvalidCastException($"Cannot cast field type {resultType} as {memberType} for class {classType} {memberTypeName} {name}");
            }

            var handler = isProp ? Harmony12.FastAccess.CreateGetterHandler(propInfo) : Harmony12.FastAccess.CreateGetterHandler(fieldInfo);
            return new FastGetter(handler);
        }

        public static FastGetter<TClass, TResult> CreateGetter<TClass, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var handler = CreateGetter(classType, resultType, name);
            return instance => (TResult) handler.Invoke(instance);
        }

        public static FastSetter CreateSetter(Type classType, Type valueType, string name) {
            var propertyInfo = Harmony12.AccessTools.Property(classType, name);
            var fieldInfo = Harmony12.AccessTools.Field(classType, name);
            if (propertyInfo == null && fieldInfo == null) {
                throw new Exception($"{classType} does not contain a field or property {name}");
            }

            var isProperty = propertyInfo != null;
            var memberType = isProperty ? propertyInfo.PropertyType : fieldInfo.FieldType;
            var memberTypeName = isProperty ? "property" : "field";
            if (!valueType.IsAssignableFrom(memberType) && (!memberType.IsEnum || valueType != typeof(int))) {
                throw new Exception($"Cannot cast property type {valueType} as {memberType} for class {classType} {memberTypeName} {name}");
            }

            var handler = isProperty ? Harmony12.FastAccess.CreateSetterHandler(propertyInfo) : Harmony12.FastAccess.CreateSetterHandler(fieldInfo);
            return new FastSetter(handler);
        }

        public static FastSetter<TClass, TValue> CreateSetter<TClass, TValue>(string name) {
            var classType = typeof(TClass);
            var valueType = typeof(TValue);
            var handler = CreateSetter(classType, valueType, name);
            return (instance, value) => handler.Invoke(instance, value);
        }

        private static MethodInfo GetMethodInfoValidated(Type classType, string name, Type resultType, Type[] args, Type[] typeArgs) {
            var methodInfo = Harmony12.AccessTools.Method(classType, name, args, typeArgs);
            if (methodInfo == null) {
                var argString = string.Join(", ", args.Select(t => t.ToString()));
                throw new Exception($"{classType} does not contain method {name} with arguments {argString}");
            }

            if (!resultType.IsAssignableFrom(methodInfo.ReturnType)) {
                throw new Exception($"Cannot cast return type {resultType} as {methodInfo.ReturnType} for class {classType} method {name}");
            }

            return methodInfo;
        }

        private static FastInvoker CreateInvoker(Type classType, string name, Type resultType, Type[] args, Type[] typeArgs = null) {
            var methodInfo = GetMethodInfoValidated(classType, name, resultType, args, typeArgs);
            return new FastInvoker(Harmony12.MethodInvoker.GetHandler(methodInfo));
        }

        public static FastInvoker<TClass, TResult> CreateInvoker<TClass, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var args = new Type[] { };
            var invoker = CreateInvoker(classType, name, resultType, args);
            return instance => (TResult) invoker.Invoke(instance);
        }

        public static FastInvoker<TClass, T1, TResult> CreateInvoker<TClass, T1, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1)};
            var invoker = CreateInvoker(classType, name, resultType, args);
            return (instance, arg1) => (TResult) invoker.Invoke(instance, arg1);
        }

        public static FastInvoker<TClass, T1, T2, TResult> CreateInvoker<TClass, T1, T2, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1), typeof(T2)};
            var invoker = CreateInvoker(classType, name, resultType, args);
            return (instance, arg1, arg2) => (TResult) invoker.Invoke(instance, arg1, arg2);
        }

        public static FastInvoker<TClass, T1, T2, T3, TResult> CreateInvoker<TClass, T1, T2, T3, TResult>(string name) {
            var classType = typeof(TClass);
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1), typeof(T2), typeof(T3)};
            var invoker = CreateInvoker(classType, name, resultType, args);
            return (instance, arg1, arg2, arg3) => (TResult) invoker.Invoke(instance, arg1, arg2, arg3);
        }

        private class StaticFastInvokeHandler {
            private readonly Type classType;
            private readonly Harmony12.FastInvokeHandler invoker;

            public StaticFastInvokeHandler(Type classType, Harmony12.FastInvokeHandler invoker) {
                this.classType = classType;
                this.invoker = invoker;
            }

            public object Invoke(params object[] args) {
                return invoker.Invoke(classType, args);
            }
        }

        private static FastStaticInvoker CreateStaticInvoker(Type classType, string name, Type resultType, Type[] args, Type[] typeArgs = null) {
            var methodInfo = GetMethodInfoValidated(classType, name, resultType, args, typeArgs);
            return new StaticFastInvokeHandler(classType, Harmony12.MethodInvoker.GetHandler(methodInfo)).Invoke;
        }

        public static FastStaticInvoker<TResult> CreateStaticInvoker<TResult>(Type classType, string name) {
            var resultType = typeof(TResult);
            var args = new Type[] { };
            var invoker = CreateStaticInvoker(classType, name, resultType, args);
            return () => (TResult) invoker.Invoke();
        }

        public static FastStaticInvoker<T1, TResult> CreateStaticInvoker<T1, TResult>(Type classType, string name) {
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1)};
            var invoker = CreateStaticInvoker(classType, name, resultType, args);
            return (arg1) => (TResult) invoker.Invoke(arg1);
        }

        public static FastStaticInvoker<T1, T2, TResult> CreateStaticInvoker<T1, T2, TResult>(Type classType, string name) {
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1), typeof(T2)};
            var invoker = CreateStaticInvoker(classType, name, resultType, args);
            return (arg1, arg2) => (TResult) invoker.Invoke(arg1, arg2);
        }

        public static FastStaticInvoker<T1, T2, T3, TResult> CreateStaticInvoker<T1, T2, T3, TResult>(Type classType, string name) {
            var resultType = typeof(TResult);
            var args = new[] {typeof(T1), typeof(T2), typeof(T3)};
            var invoker = CreateStaticInvoker(classType, name, resultType, args);
            return (arg1, arg2, arg3) => (TResult) invoker.Invoke(arg1, arg2, arg3);
        }
    }
}