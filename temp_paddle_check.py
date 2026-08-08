import paddle
import sys
print(sys.executable)
print('paddle file:', paddle.__file__)
print('paddle version:', paddle.__version__)
print('compiledWithCuda:', getattr(paddle.device, 'is_compiled_with_cuda', lambda: False)())
print('cuda device count:', getattr(getattr(paddle.device, 'cuda', None), 'device_count', lambda: 0)())
print('get_device:', getattr(paddle.device, 'get_device', lambda: lambda: None)())
